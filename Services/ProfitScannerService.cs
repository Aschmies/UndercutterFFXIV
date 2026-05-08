using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UndercutterFFXIV.Models;
using Dalamud.Plugin.Services;

namespace UndercutterFFXIV.Services
{
    public enum ScanMode { Watchlist, VelocityThreshold, TopItems }

    public sealed class ProfitScannerService
    {
        private readonly IDataManager dataManager;
        private readonly UniversalisMarketClient universalis;
        private readonly MarketMasterDatabase database;
        private readonly Configuration config;

        private readonly object cacheLock = new();
        private List<ItemLookup>? itemCache;
        private List<ArbitrageOpportunity> lastResults = new();

        private ScanMode currentScanMode = ScanMode.Watchlist;
        private string? currentScanCategory;
        private int topItemsCount = 200;

        public ProfitScannerService(
            IDataManager dataManager,
            UniversalisMarketClient universalis,
            MarketMasterDatabase database,
            Configuration config)
        {
            this.dataManager = dataManager;
            this.universalis = universalis;
            this.database = database;
            this.config = config;
        }

        public IReadOnlyList<ItemLookup> SearchItems(string query, int limit = 100)
        {
            EnsureItemCacheLoaded();
            if (itemCache == null || itemCache.Count == 0)
                return Array.Empty<ItemLookup>();

            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
                return itemCache.Take(limit).ToList();

            return itemCache
                .Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Max(1, limit))
                .ToList();
        }

        public IReadOnlyList<WatchedItem> GetWatchlist() => database.GetWatchedItems();

        public void AddWatchItem(ItemLookup item) => database.AddOrUpdateWatchedItem(item.ItemId, item.Name);

        public void RemoveWatchItem(uint itemId) => database.RemoveWatchedItem(itemId);

        public IReadOnlyList<ArbitrageOpportunity> GetLastResults()
        {
            lock (cacheLock)
                return lastResults.ToList();
        }

        public ApiHealthSnapshot GetApiHealth() => universalis.GetHealthSnapshot();

        public IReadOnlyList<(DateTime DateUtc, double TotalNetProfit)> GetProfitSeries(int days) =>
            database.GetDailyProfitSeries(days);

        public void SetScanMode(ScanMode mode, string? categoryName = null, int topCount = 200)
        {
            currentScanMode = mode;
            currentScanCategory = categoryName;
            topItemsCount = topCount;
        }

        public ScanMode GetCurrentScanMode() => currentScanMode;
        public string? GetCurrentCategory() => currentScanCategory;
        public int GetTopItemsCount() => topItemsCount;

        public IReadOnlyList<string> GetItemCategories()
        {
            // Category scanning simplified - returns watchlist only for now
            return new[] { "Watchlist" };
        }

        private List<ItemLookup> GetItemsForScan()
        {
            EnsureItemCacheLoaded();
            if (itemCache == null || itemCache.Count == 0)
                return new List<ItemLookup>();

            return currentScanMode switch
            {
                ScanMode.Watchlist 
                    => database.GetWatchedItems()
                        .Select(w => new ItemLookup { ItemId = w.ItemId, Name = w.Name })
                        .ToList(),
                ScanMode.VelocityThreshold
                    => GetHighVelocityItems(config.MinSaleVelocityPerDay * 2),
                ScanMode.TopItems
                    => GetTopTradedItems(topItemsCount),
                _ => database.GetWatchedItems()
                    .Select(w => new ItemLookup { ItemId = w.ItemId, Name = w.Name })
                    .ToList()
            };
        }

        private List<ItemLookup> GetHighVelocityItems(double minVelocity)
        {
            EnsureItemCacheLoaded();
            return itemCache?.Take(500).ToList() ?? new List<ItemLookup>();
        }

        private List<ItemLookup> GetTopTradedItems(int count)
        {
            EnsureItemCacheLoaded();
            return itemCache?.Take(Math.Min(count, itemCache.Count)).ToList() ?? new List<ItemLookup>();
        }

        public async Task<IReadOnlyList<ArbitrageOpportunity>> ScanWatchlistAsync(CancellationToken cancellationToken)
        {
            var itemsToScan = GetItemsForScan();
            var results = new List<ArbitrageOpportunity>();

            if (itemsToScan.Count == 0)
            {
                lock (cacheLock) lastResults = new List<ArbitrageOpportunity>();
                return lastResults;
            }

            var sw = Stopwatch.StartNew();

            foreach (var item in itemsToScan)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var home = await universalis.GetMarketSnapshotAsync(config.WorldName, item.ItemId, cancellationToken);
                var dc = await universalis.GetMarketSnapshotAsync(config.DataCenterName, item.ItemId, cancellationToken);

                if (home == null || dc == null)
                    continue;
                if (home.LowestPrice == 0 || dc.LowestPrice == 0)
                    continue;

                var velocity = CalculateSaleVelocity(home.RecentSales, config.ScannerLookbackDays);
                if (velocity < config.MinSaleVelocityPerDay)
                    continue;

                var netProfit = (home.LowestPrice * (1 - (config.MarketTaxRatePercent / 100.0))) - dc.LowestPrice;
                var profitPercent = dc.LowestPrice == 0 ? 0 : (netProfit / dc.LowestPrice) * 100;

                if (netProfit < config.MinNetProfitGil || profitPercent < config.MinNetProfitPercent)
                    continue;

                var botPattern = DetectPotentialBotPattern(home.Listings);

                results.Add(new ArbitrageOpportunity
                {
                    ItemId = item.ItemId,
                    ItemName = item.Name,
                    HomeWorldMinPrice = home.LowestPrice,
                    DataCenterLowestPrice = dc.LowestPrice,
                    NetProfitPerUnit = netProfit,
                    ProfitPercent = profitPercent,
                    SaleVelocityPerDay = velocity,
                    PotentialBotSellerPattern = botPattern,
                    SafeBuyQty = ArbitrageOpportunity.ComputeSafeBuyQty(velocity, botPattern),
                    ScannedUtc = DateTime.UtcNow
                });
            }

            sw.Stop();
            var sorted = results
                .OrderByDescending(r => r.NetProfitPerUnit)
                .ThenByDescending(r => r.SaleVelocityPerDay)
                .ToList();

            database.SaveScanResults(config.WorldName, config.DataCenterName, sorted, sw.ElapsedMilliseconds);

            lock (cacheLock)
                lastResults = sorted;

            return sorted;
        }

        public async Task<uint> FetchHomeFloorPriceAsync(uint itemId, CancellationToken cancellationToken)
        {
            var home = await universalis.GetMarketSnapshotAsync(config.WorldName, itemId, cancellationToken);
            return home?.LowestPrice ?? 0;
        }

        private void EnsureItemCacheLoaded()
        {
            lock (cacheLock)
            {
                if (itemCache != null)
                    return;

                var loaded = new List<ItemLookup>(32000);
                var sheet = dataManager.GetExcelSheet<Item>();
                if (sheet != null)
                {
                    foreach (var row in sheet)
                    {
                        if (row.RowId == 0)
                            continue;

                        var name = row.Name.ToString();
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        loaded.Add(new ItemLookup
                        {
                            ItemId = row.RowId,
                            Name = name
                        });
                    }
                }

                itemCache = loaded;
                LoggingService.LogInfo($"Loaded {loaded.Count} items into search cache");
            }
        }

        private static double CalculateSaleVelocity(IReadOnlyList<SaleRecord> sales, int lookbackDays)
        {
            if (sales.Count == 0)
                return 0;

            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, lookbackDays));
            var relevant = sales.Where(s => s.TimestampUtc >= cutoff).ToList();
            if (relevant.Count == 0)
                return 0;

            return relevant.Count / (double)Math.Max(1, lookbackDays);
        }

        private static bool DetectPotentialBotPattern(IReadOnlyList<ListingRecord> listings)
        {
            if (listings.Count < 6)
                return false;

            var suspiciousGroup = listings
                .Where(l => !string.IsNullOrWhiteSpace(l.SellerName))
                .GroupBy(l => (l.SellerName, l.PricePerUnit))
                .Any(g => g.Count() >= 3);

            return suspiciousGroup;
        }
    }
}
