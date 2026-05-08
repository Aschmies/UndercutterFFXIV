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
        private readonly RetainerPriceService retainerPriceService;

        private readonly object cacheLock = new();
        private List<ItemLookup>? itemCache;
        private List<ItemLookup>? marketableItemCache;
        private List<ArbitrageOpportunity> lastResults = new();
        private bool scanInProgress;
        private int scanProcessedItems;
        private int scanTotalItems;

        private ScanMode currentScanMode = ScanMode.TopItems;
        private string? currentScanCategory;
        private int topItemsCount = 250;

        public ProfitScannerService(
            IDataManager dataManager,
            UniversalisMarketClient universalis,
            MarketMasterDatabase database,
            Configuration config,
            RetainerPriceService retainerPriceService)
        {
            this.dataManager = dataManager;
            this.universalis = universalis;
            this.database = database;
            this.config = config;
            this.retainerPriceService = retainerPriceService;
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

        public IReadOnlyList<WatchedItem> GetWatchlist()
        {
            var manualItems = database.GetWatchedItems();
            if (!config.AutoTrackCurrentlySellingItems)
                return manualItems;

            var combined = manualItems.ToDictionary(item => item.ItemId);
            foreach (var liveItem in retainerPriceService.GetCurrentSellingItems())
            {
                if (combined.ContainsKey(liveItem.ItemId))
                    continue;

                combined[liveItem.ItemId] = new WatchedItem
                {
                    ItemId = liveItem.ItemId,
                    Name = liveItem.Name,
                    AddedUtc = DateTime.UtcNow,
                    IsAutoTracked = true
                };
            }

            return combined.Values
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

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

        public (bool IsRunning, int Processed, int Total) GetScanProgress()
        {
            lock (cacheLock)
                return (scanInProgress, scanProcessedItems, scanTotalItems);
        }

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

            var watchlistItems = GetWatchlist()
                .Select(w => new ItemLookup { ItemId = w.ItemId, Name = w.Name })
                .ToList();

            return currentScanMode switch
            {
                ScanMode.Watchlist
                    => watchlistItems,
                ScanMode.VelocityThreshold
                    => MergeScanCandidates(watchlistItems, GetHighVelocityItems(config.MinSaleVelocityPerDay * 2)),
                ScanMode.TopItems
                    => MergeScanCandidates(watchlistItems, GetTopTradedItems(topItemsCount)),
                _ => watchlistItems
            };
        }

        private List<ItemLookup> GetHighVelocityItems(double minVelocity)
        {
            // Universalis velocity is not cached locally, so use a broad marketable sample.
            // This still produces actionable scan candidates instead of the old placeholder first-row slice.
            var targetCount = minVelocity >= 4 ? 250 : 500;
            return GetMarketableSample(targetCount);
        }

        private List<ItemLookup> GetTopTradedItems(int count)
        {
            return GetMarketableSample(Math.Max(10, count));
        }

        private List<ItemLookup> MergeScanCandidates(
            IReadOnlyList<ItemLookup> primary,
            IReadOnlyList<ItemLookup> secondary)
        {
            var merged = new List<ItemLookup>(primary.Count + secondary.Count);
            var seen = new HashSet<uint>();

            foreach (var item in primary)
            {
                if (!seen.Add(item.ItemId))
                    continue;

                merged.Add(item);
            }

            foreach (var item in secondary)
            {
                if (!seen.Add(item.ItemId))
                    continue;

                merged.Add(item);
            }

            return merged;
        }

        private List<ItemLookup> GetMarketableSample(int desiredCount)
        {
            EnsureItemCacheLoaded();
            if (marketableItemCache == null || marketableItemCache.Count == 0)
                return new List<ItemLookup>();

            var count = Math.Min(Math.Max(1, desiredCount), marketableItemCache.Count);
            if (count >= marketableItemCache.Count)
                return marketableItemCache.ToList();

            var step = marketableItemCache.Count / (double)count;
            var sample = new List<ItemLookup>(count);
            var seen = new HashSet<uint>();

            for (var index = 0; index < count; index++)
            {
                var sourceIndex = Math.Min(marketableItemCache.Count - 1, (int)Math.Floor(index * step));
                var item = marketableItemCache[sourceIndex];
                if (!seen.Add(item.ItemId))
                    continue;

                sample.Add(item);
            }

            if (sample.Count < count)
            {
                foreach (var item in marketableItemCache)
                {
                    if (!seen.Add(item.ItemId))
                        continue;

                    sample.Add(item);
                    if (sample.Count >= count)
                        break;
                }
            }

            return sample;
        }

        public async Task<IReadOnlyList<ArbitrageOpportunity>> ScanWatchlistAsync(CancellationToken cancellationToken)
        {
            var itemsToScan = GetItemsForScan();
            var results = new List<ArbitrageOpportunity>();

            lock (cacheLock)
            {
                scanInProgress = true;
                scanProcessedItems = 0;
                scanTotalItems = itemsToScan.Count;
            }

            if (itemsToScan.Count == 0)
            {
                lock (cacheLock)
                {
                    lastResults = new List<ArbitrageOpportunity>();
                    scanInProgress = false;
                }
                return lastResults;
            }

            try
            {
                var sw = Stopwatch.StartNew();

                foreach (var item in itemsToScan)
                {
                    try
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
                    finally
                    {
                        lock (cacheLock)
                            scanProcessedItems++;
                    }
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
            finally
            {
                lock (cacheLock)
                    scanInProgress = false;
            }
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
                var marketable = new List<ItemLookup>(24000);
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

                        var item = new ItemLookup
                        {
                            ItemId = row.RowId,
                            Name = name
                        };

                        loaded.Add(item);

                        if (!row.IsUntradable)
                            marketable.Add(item);
                    }
                }

                itemCache = loaded;
                marketableItemCache = marketable
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                LoggingService.LogInfo($"Loaded {loaded.Count} items into search cache");
                LoggingService.LogInfo($"Prepared {marketableItemCache.Count} marketable scan candidates");
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
