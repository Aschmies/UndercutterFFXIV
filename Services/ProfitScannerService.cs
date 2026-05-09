using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UndercutterFFXIV.Models;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace UndercutterFFXIV.Services
{
    public enum ScanMode { Watchlist, VelocityThreshold, TopItems, GearOnly, WeaponsOnly, ArmorOnly, AccessoriesOnly, ConsumablesOnly }

    public sealed class ProfitScannerService
    {
        private const int PartialPublishBatchSize = 5;

        private readonly IDataManager dataManager;
        private readonly UniversalisMarketClient universalis;
        private readonly MarketMasterDatabase database;
        private readonly Configuration config;
        private readonly RetainerPriceService retainerPriceService;

        private readonly object cacheLock = new();
        private List<ItemLookup>? itemCache;
        private List<ItemLookup>? marketableItemCache;
        private List<ItemLookup>? gearItemCache;
        private List<ItemLookup>? weaponItemCache;
        private List<ItemLookup>? armorItemCache;
        private List<ItemLookup>? accessoryItemCache;
        private List<ItemLookup>? consumableItemCache;
        private List<ArbitrageOpportunity> lastResults = new();
        private ScanTimingSnapshot lastScanTiming = new();
        private bool scanInProgress;
        private int scanProcessedItems;
        private int scanTotalItems;
        private int scanProgressPercent;

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

            lock (cacheLock)
                lastResults = database.GetLatestOpportunities().ToList();
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
        public ScanTimingSnapshot GetLastScanTiming()
        {
            lock (cacheLock)
                return lastScanTiming;
        }

        public IReadOnlyList<(DateTime DateUtc, double TotalNetProfit)> GetProfitSeries(int days) =>
            database.GetDailyProfitSeries(days);

        public IReadOnlyList<TradeHistoryEntry> GetTradeHistory(int days = 30)
            => database.GetTradeHistory(days);

        public void ApplyScanProfile(string profile)
        {
            var normalized = (profile ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "quick":
                    config.ActiveScanProfile = "Quick";
                    config.MinSaleVelocityPerDay = 2.5;
                    config.MinNetProfitGil = 120;
                    config.MinNetProfitPercent = 4;
                    config.MinUnitsSold24h = 3;
                    break;
                case "conservative":
                    config.ActiveScanProfile = "Conservative";
                    config.MinSaleVelocityPerDay = 2.0;
                    config.MinNetProfitGil = 220;
                    config.MinNetProfitPercent = 9;
                    config.MinUnitsSold24h = 5;
                    break;
                case "highvolume":
                case "high-volume":
                    config.ActiveScanProfile = "HighVolume";
                    config.MinSaleVelocityPerDay = 3.5;
                    config.MinNetProfitGil = 80;
                    config.MinNetProfitPercent = 2.5;
                    config.MinUnitsSold24h = 8;
                    break;
                default:
                    config.ActiveScanProfile = "Balanced";
                    config.MinSaleVelocityPerDay = 2.0;
                    config.MinNetProfitGil = 100;
                    config.MinNetProfitPercent = 5;
                    config.MinUnitsSold24h = 0;
                    break;
            }
        }

        public void AddTradeHistoryEntry(uint itemId, string itemName, uint buyPrice, uint sellPrice, uint quantity)
        {
            database.AddTradeHistoryEntry(new TradeHistoryEntry
            {
                ItemId = itemId,
                ItemName = itemName,
                BuyPrice = buyPrice,
                SellPrice = sellPrice,
                Quantity = quantity,
                TradedUtc = DateTime.UtcNow
            });
        }

        public void RecordRecommendationFeedback(ArbitrageOpportunity opportunity, bool accepted)
            => database.SaveRecommendationFeedback(opportunity.ItemId, opportunity.ItemName, accepted, opportunity.ProjectedBatchNetGil);

        public RecommendationFeedbackSummary GetRecommendationFeedbackSummary(int days = 30)
            => database.GetRecommendationFeedbackSummary(days);

        public IReadOnlyList<(string World, double ProjectedNetGil, int ItemCount)> GetTravelBatchPlans(IReadOnlyList<ArbitrageOpportunity> opportunities, int limit = 3)
        {
            return opportunities
                .Where(opp => !string.IsNullOrWhiteSpace(opp.BuyFromWorld)
                    && !string.Equals(opp.BuyFromWorld, config.WorldName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(opp => opp.BuyFromWorld)
                .Select(group => (
                    World: group.Key,
                    ProjectedNetGil: group.Sum(opp => opp.ProjectedBatchNetGil) - config.WorldTravelOverheadGil,
                    ItemCount: group.Count()))
                .OrderByDescending(x => x.ProjectedNetGil)
                .ThenByDescending(x => x.ItemCount)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        public IReadOnlyList<CapitalAllocationPlanItem> GetCapitalAllocationPlan(IReadOnlyList<ArbitrageOpportunity> opportunities, int limit = 20)
        {
            var budgetRemaining = Math.Max(1, config.MaxCapitalPerDayGil);
            var perItemCap = Math.Max(1, config.MaxCapitalPerItemGil);
            var plan = new List<CapitalAllocationPlanItem>();

            foreach (var opportunity in opportunities
                .OrderByDescending(ComputeAllocationScore)
                .ThenByDescending(opp => opp.ProjectedBatchNetGil)
                .Take(Math.Max(1, limit)))
            {
                if (budgetRemaining <= 0)
                    break;

                var unitPrice = Math.Max(1.0, opportunity.DataCenterLowestPrice);
                var maxByItemCap = Math.Max(1, (int)Math.Floor(perItemCap / unitPrice));
                var maxByRemaining = Math.Max(1, (int)Math.Floor(budgetRemaining / unitPrice));
                var qty = Math.Min(opportunity.RecommendedBuyQty, Math.Min(maxByItemCap, maxByRemaining));
                if (qty <= 0)
                    continue;

                var allocatedCost = qty * unitPrice;
                budgetRemaining -= (int)Math.Round(allocatedCost);

                plan.Add(new CapitalAllocationPlanItem
                {
                    ItemId = opportunity.ItemId,
                    ItemName = opportunity.ItemName,
                    Score = ComputeAllocationScore(opportunity),
                    AllocatedQty = qty,
                    UnitBuyPrice = unitPrice,
                    AllocatedCostGil = allocatedCost,
                    ProjectedNetGil = qty * opportunity.NetProfitPerUnit,
                    BuyFromWorld = opportunity.BuyFromWorld,
                    ProfitPercent = opportunity.ProfitPercent
                });
            }

            return plan;
        }

        public QueueSimulationResult SimulateOpportunityQueue(IReadOnlyList<CapitalAllocationPlanItem> plan)
        {
            if (plan.Count == 0)
                return new QueueSimulationResult();

            var totalCost = plan.Sum(item => item.AllocatedCostGil);
            var baseNet = plan.Sum(item => item.ProjectedNetGil);

            // Best/worst cases model spread and fill uncertainty while keeping a conservative downside floor.
            var bestNet = baseNet * 1.35;
            var worstNet = (baseNet * 0.45) - (totalCost * 0.03);
            var expectedValue = plan.Sum(item =>
            {
                var confidenceWeight = Math.Clamp(item.Score / 100.0, 0.20, 0.90);
                var downside = item.ProjectedNetGil * 0.45;
                return (item.ProjectedNetGil * confidenceWeight) + (downside * (1 - confidenceWeight));
            });

            var delayHours = Math.Max(0.5, (plan.Count * 0.6) + (plan.Sum(item => item.AllocatedQty) / 12.0));

            return new QueueSimulationResult
            {
                ItemCount = plan.Count,
                TotalCostGil = totalCost,
                BestCaseNetGil = bestNet,
                BaseCaseNetGil = baseNet,
                WorstCaseNetGil = worstNet,
                ExpectedValueNetGil = expectedValue,
                EstimatedLiquidityDelayHours = delayHours
            };
        }

        public async Task<IReadOnlyList<OpportunityRejectionReason>> ExplainWatchlistExclusionsAsync(CancellationToken cancellationToken)
        {
            var watched = GetWatchlist().ToList();
            if (watched.Count == 0)
                return Array.Empty<OpportunityRejectionReason>();

            var ids = watched.Select(item => item.ItemId).Where(id => id > 0).Distinct().ToList();
            var homeSnapshots = await universalis.GetMarketSnapshotsAsync(config.WorldName, ids, cancellationToken);
            var dcSnapshots = await universalis.GetMarketSnapshotsAsync(config.DataCenterName, ids, cancellationToken);
            var health = universalis.GetHealthSnapshot();

            var exclusions = new List<OpportunityRejectionReason>();
            foreach (var item in watched)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var reason = EvaluateExclusionReason(item, homeSnapshots, dcSnapshots, health);
                if (string.IsNullOrWhiteSpace(reason))
                    continue;

                exclusions.Add(new OpportunityRejectionReason
                {
                    ItemId = item.ItemId,
                    ItemName = item.Name,
                    Reason = reason
                });
            }

            return exclusions
                .OrderBy(entry => entry.ItemName, StringComparer.OrdinalIgnoreCase)
                .Take(60)
                .ToList();
        }

        public IReadOnlyList<WatchlistSuggestion> GetWatchlistSuggestions(int limit = 8)
        {
            EnsureItemCacheLoaded();
            if (marketableItemCache == null || marketableItemCache.Count == 0)
                return Array.Empty<WatchlistSuggestion>();

            var watched = GetWatchlist().Select(w => w.ItemId).ToHashSet();
            var winners = GetTradeHistory(30)
                .Where(entry => entry.SellPrice > entry.BuyPrice)
                .GroupBy(entry => CategorizeItem(entry.ItemName))
                .OrderByDescending(group => group.Count())
                .Take(3)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (winners.Count == 0)
                winners.Add("Materials");

            var results = new List<WatchlistSuggestion>();
            foreach (var item in marketableItemCache)
            {
                if (watched.Contains(item.ItemId))
                    continue;

                var category = CategorizeItem(item.Name);
                if (!winners.Contains(category))
                    continue;

                results.Add(new WatchlistSuggestion
                {
                    ItemId = item.ItemId,
                    ItemName = item.Name,
                    Reason = $"Similar to profitable {category} trades"
                });

                if (results.Count >= Math.Max(1, limit))
                    break;
            }

            return results;
        }

        public string ExportSessionReport(
            string outputDirectory,
            IReadOnlyList<ArbitrageOpportunity> opportunities,
            int queuedItems,
            int inventoryRows)
        {
            Directory.CreateDirectory(outputDirectory);
            var now = DateTime.UtcNow;
            var analytics = GetAdvancedHistoryAnalytics(30);
            var feedback = GetRecommendationFeedbackSummary(30);
            var path = Path.Combine(outputDirectory, $"session_report_{now:yyyyMMdd_HHmmss}.txt");

            var lines = new List<string>
            {
                "Market Master Pro Session Report",
                $"UTC: {now:O}",
                $"Profile: {config.ActiveScanProfile}",
                $"Opportunities: {opportunities.Count}",
                $"Inventory Rows: {inventoryRows}",
                $"Queued Reprices: {queuedItems}",
                $"Win Rate (30d): {analytics.WinRatePercent:F1}%",
                $"Avg Net/Hr (30d): {analytics.AverageNetGilPerHour:N0}",
                $"Recommendation Acceptance (30d): {feedback.AcceptanceRatePercent:F1}% ({feedback.AcceptedCount}/{feedback.AcceptedCount + feedback.RejectedCount})",
                string.Empty,
                "Top Opportunities"
            };

            foreach (var opp in opportunities
                .OrderByDescending(o => o.ProjectedBatchNetGil)
                .ThenByDescending(o => o.ConfidenceScore)
                .Take(12))
            {
                lines.Add($"- {opp.ItemName} | Qty {opp.RecommendedBuyQty} | Net/Unit {opp.NetProfitPerUnit:N0} | Batch {opp.ProjectedBatchNetGil:N0} | Confidence {opp.ConfidenceScore:F0} | Regime {opp.RiskRegime}");
            }

            File.WriteAllLines(path, lines);
            return path;
        }

        public IReadOnlyList<PendingBuyCaptureEntry> GetPendingBuyCaptures(int limit = 100)
            => database.GetPendingBuyCaptures(limit);

        public bool ConfirmPendingBuyCapture(
            ulong listingId,
            uint itemId,
            string itemName,
            uint quantity,
            uint buyPrice,
            uint sellPrice)
        {
            if (string.IsNullOrWhiteSpace(itemName) || quantity == 0 || buyPrice == 0 || sellPrice == 0)
                return false;

            AddTradeHistoryEntry(itemId, itemName.Trim(), buyPrice, sellPrice, quantity);
            database.RemovePendingBuyCapture(listingId);
            return true;
        }

        public void DismissPendingBuyCapture(ulong listingId)
            => database.RemovePendingBuyCapture(listingId);

        public int CapturePendingBuysFromClientCache()
        {
            if (!config.EnableAutoBuyHistoryCapture)
                return 0;

            unsafe
            {
                var infoModule = InfoModule.Instance();
                if (infoModule == null)
                    return 0;

                var proxy = (InfoProxyItemSearch*)infoModule->GetInfoProxyById(InfoProxyId.ItemSearch);
                if (proxy == null)
                    return 0;

                var last = proxy->LastPurchasedMarketboardItem;
                if (!last.Present || last.ListingId == 0 || last.ItemId == 0)
                    return 0;

                var itemName = ResolveItemName(last.ItemId);
                var entry = new PendingBuyCaptureEntry
                {
                    ListingId = last.ListingId,
                    ItemId = last.ItemId,
                    ItemName = itemName,
                    Quantity = Math.Max(1u, last.Quantity),
                    UnitPrice = last.UnitPrice,
                    TotalTax = last.TotalTax,
                    ContainerIndex = last.ContainerIndex,
                    IsHq = last.IsHqItem,
                    TownId = last.TownId,
                    CapturedUtc = DateTime.UtcNow
                };

                if (!database.UpsertPendingBuyCapture(entry))
                    return 0;

                if (config.AutoBuyHistoryAutoConfirm)
                {
                    AddTradeHistoryEntry(entry.ItemId, entry.ItemName, entry.UnitPrice, entry.UnitPrice, entry.Quantity);
                    database.RemovePendingBuyCapture(entry.ListingId);
                }

                return 1;
            }
        }

        public void SaveRetainerListingSnapshots(IReadOnlyList<RetainerListingSnapshot> snapshots)
            => database.SaveRetainerListingSnapshots(snapshots);

        public IReadOnlyList<RetainerListingSnapshot> GetRetainerListingSnapshots(int days = 30)
            => database.GetRetainerListingSnapshots(days);

        public RetainerSnapshotAnalytics GetRetainerSnapshotAnalytics(int days = 30)
        {
            var snapshots = database.GetRetainerListingSnapshots(days)
                .OrderBy(snapshot => snapshot.ScannedUtc)
                .ToList();

            if (snapshots.Count == 0)
                return new RetainerSnapshotAnalytics();

            var undercutCount = snapshots.Count(snapshot => snapshot.IsUndercut);
            var groupedBySlot = snapshots
                .GroupBy(snapshot => (snapshot.ItemId, snapshot.SlotIndex));

            var sitDurations = groupedBySlot
                .Select(group => (group.Max(x => x.ScannedUtc) - group.Min(x => x.ScannedUtc)).TotalHours)
                .Where(hours => hours >= 0)
                .ToList();

            var churn = snapshots
                .GroupBy(snapshot => snapshot.ItemId)
                .Select(group =>
                {
                    var ordered = group.OrderBy(x => x.ScannedUtc).ToList();
                    var changes = 0;
                    uint previous = 0;
                    foreach (var snapshot in ordered)
                    {
                        if (previous > 0 && snapshot.CurrentPrice != previous)
                            changes++;
                        previous = snapshot.CurrentPrice;
                    }

                    return (
                        ItemName: ordered[0].ItemName,
                        PriceChanges: changes);
                })
                .OrderByDescending(x => x.PriceChanges)
                .Take(5)
                .ToList();

            return new RetainerSnapshotAnalytics
            {
                TotalSnapshots = snapshots.Count,
                UndercutFrequencyPercent = (undercutCount * 100.0) / snapshots.Count,
                AverageSitHours = sitDurations.Count == 0 ? 0 : sitDurations.Average(),
                FastestChurnItems = churn
            };
        }

        public AdvancedHistoryAnalytics GetAdvancedHistoryAnalytics(int days = 30)
        {
            var entries = GetTradeHistory(days)
                .OrderBy(entry => entry.TradedUtc)
                .ToList();
            if (entries.Count == 0)
                return new AdvancedHistoryAnalytics();

            var taxMultiplier = 1.0 - (config.MarketTaxRatePercent / 100.0);
            var netEntries = entries
                .Select(entry => new
                {
                    Entry = entry,
                    NetTotal = ((entry.SellPrice * taxMultiplier) - entry.BuyPrice) * entry.Quantity,
                    Category = CategorizeItem(entry.ItemName)
                })
                .ToList();

            var wins = netEntries.Count(x => x.NetTotal > 0);
            var firstUtc = entries.First().TradedUtc;
            var lastUtc = entries.Last().TradedUtc;
            var hours = Math.Max(1.0, (lastUtc - firstUtc).TotalHours);
            var totalNet = netEntries.Sum(x => x.NetTotal);

            var bestCategories = netEntries
                .GroupBy(x => x.Category)
                .Select(group => (Category: group.Key, NetGil: group.Sum(x => x.NetTotal)))
                .OrderByDescending(x => x.NetGil)
                .Take(5)
                .ToList();

            var repeatedLoss = netEntries
                .Where(x => x.NetTotal < 0)
                .GroupBy(x => x.Entry.ItemName)
                .Select(group =>
                    (ItemName: group.Key,
                     LossCount: group.Count(),
                     TotalLoss: group.Sum(x => x.NetTotal)))
                .Where(x => x.LossCount >= 2)
                .OrderBy(x => x.TotalLoss)
                .Take(5)
                .ToList();

            var snapshotAnalytics = GetRetainerSnapshotAnalytics(days);

            return new AdvancedHistoryAnalytics
            {
                TotalTrades = entries.Count,
                WinRatePercent = (wins * 100.0) / entries.Count,
                AverageNetGilPerHour = totalNet / hours,
                MedianEstimatedHoldHours = snapshotAnalytics.AverageSitHours,
                BestCategories = bestCategories,
                RepeatedLossItems = repeatedLoss
            };
        }

        public (bool IsRunning, int Processed, int Total, int Percent) GetScanProgress()
        {
            lock (cacheLock)
            return (scanInProgress, scanProcessedItems, scanTotalItems, scanProgressPercent);
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
            return GetItemsForMode(currentScanMode, topItemsCount);
        }

        private List<ItemLookup> GetItemsForMode(ScanMode mode, int topCount)
        {
            EnsureItemCacheLoaded();
            if (itemCache == null || itemCache.Count == 0)
                return new List<ItemLookup>();

            var watchlistItems = GetWatchlist()
                .Select(w => new ItemLookup { ItemId = w.ItemId, Name = w.Name })
                .ToList();

            return mode switch
            {
                ScanMode.Watchlist
                    => watchlistItems,
                ScanMode.VelocityThreshold
                    => MergeScanCandidates(watchlistItems, GetHighVelocityItems(config.MinSaleVelocityPerDay * 2)),
                ScanMode.TopItems
                    => MergeScanCandidates(watchlistItems, GetTopTradedItems(topCount)),
                ScanMode.GearOnly
                    => GetGearItems(),
                ScanMode.WeaponsOnly
                    => GetWeaponItems(),
                ScanMode.ArmorOnly
                    => GetArmorItems(),
                ScanMode.AccessoriesOnly
                    => GetAccessoryItems(),
                ScanMode.ConsumablesOnly
                    => GetConsumableItems(),
                _ => watchlistItems
            };
        }

        private List<ItemLookup> GetGearItems()
        {
            EnsureItemCacheLoaded();
            return gearItemCache?.ToList() ?? new List<ItemLookup>();
        }

        private List<ItemLookup> GetWeaponItems()
        {
            EnsureItemCacheLoaded();
            return weaponItemCache?.ToList() ?? new List<ItemLookup>();
        }

        private List<ItemLookup> GetArmorItems()
        {
            EnsureItemCacheLoaded();
            return armorItemCache?.ToList() ?? new List<ItemLookup>();
        }

        private List<ItemLookup> GetAccessoryItems()
        {
            EnsureItemCacheLoaded();
            return accessoryItemCache?.ToList() ?? new List<ItemLookup>();
        }

        private List<ItemLookup> GetConsumableItems()
        {
            EnsureItemCacheLoaded();
            if (consumableItemCache == null || consumableItemCache.Count == 0)
                return new List<ItemLookup>();

            // Limit to top 250 consumables to avoid excessive API batches (~100 requests for 9000+ items)
            // while still covering potions, food, materials, and common crafting ingredients
            const int consumableLimit = 250;
            if (consumableItemCache.Count <= consumableLimit)
                return consumableItemCache.ToList();

            // Sample the consumables list across its range to get diverse items
            var step = consumableItemCache.Count / (double)consumableLimit;
            var sample = new List<ItemLookup>(consumableLimit);
            var seen = new HashSet<uint>();

            for (var index = 0; index < consumableLimit; index++)
            {
                var sourceIndex = Math.Min(consumableItemCache.Count - 1, (int)Math.Floor(index * step));
                var item = consumableItemCache[sourceIndex];
                if (!seen.Add(item.ItemId))
                    continue;

                sample.Add(item);
            }

            if (sample.Count < consumableLimit)
            {
                foreach (var item in consumableItemCache)
                {
                    if (!seen.Add(item.ItemId))
                        continue;

                    sample.Add(item);
                    if (sample.Count >= consumableLimit)
                        break;
                }
            }

            return sample;
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
            return await ScanAsync(GetItemsForScan(), cancellationToken);
        }

        public async Task<IReadOnlyList<ArbitrageOpportunity>> ScanWatchlistOnlyAsync(CancellationToken cancellationToken)
        {
            return await ScanAsync(GetItemsForMode(ScanMode.Watchlist, topItemsCount), cancellationToken);
        }

        private async Task<IReadOnlyList<ArbitrageOpportunity>> ScanAsync(
            List<ItemLookup> itemsToScan,
            CancellationToken cancellationToken)
        {
            var results = new List<ArbitrageOpportunity>();
            var resultsLock = new object();

            lock (cacheLock)
            {
                if (scanInProgress)
                    return lastResults.ToList();

                scanInProgress = true;
                scanProcessedItems = 0;
                scanTotalItems = itemsToScan.Count;
                scanProgressPercent = 0;
            }

            if (itemsToScan.Count == 0)
            {
                lock (cacheLock)
                {
                    scanInProgress = false;
                }
                return GetLastResults();
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var homeFetchSw = Stopwatch.StartNew();
                var scannedSinceLastPublish = 0;
                var publishCounterLock = new object();

                var itemIds = itemsToScan
                    .Select(item => item.ItemId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                // Phase 1: batch-fetch home world snapshots for all scan items.
                var homeSnapshots = await universalis.GetMarketSnapshotsAsync(
                    config.WorldName,
                    itemIds,
                    cancellationToken,
                    (processed, total) => UpdateScanProgressByPhase(processed, total, 0, 10));
                homeFetchSw.Stop();

                // Mark fetch completion at 10%
                UpdateScanProgressPercent(10);

                var homeFilterSw = Stopwatch.StartNew();
                var homeCandidatesByItemId = new Dictionary<uint, HomeSnapshotCandidate>();
                var itemsProcessedInHomeFilter = 0;
                var homeFilterIncrementEvery = Math.Max(1, itemsToScan.Count / 30); // Distribute remaining 30% across filter

                foreach (var item in itemsToScan)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!homeSnapshots.TryGetValue(item.ItemId, out var home))
                            continue;

                        if (!TryBuildHomeSnapshotCandidate(home, currentScanMode, out var candidate))
                            continue;

                        homeCandidatesByItemId[item.ItemId] = candidate;
                    }
                    finally
                    {
                        itemsProcessedInHomeFilter++;
                        if (itemsProcessedInHomeFilter % homeFilterIncrementEvery == 0)
                            UpdateScanProgressByPhase(itemsProcessedInHomeFilter, itemsToScan.Count, 10, 40);
                    }
                }
                homeFilterSw.Stop();

                // Mark filter completion at 40%
                UpdateScanProgressPercent(40);

                // Phase 2: batch-fetch DC snapshots only for home-eligible candidates.
                var candidateIds = homeCandidatesByItemId.Keys.ToList();
                var dcFetchSw = Stopwatch.StartNew();
                var dcSnapshots = candidateIds.Count == 0
                    ? new Dictionary<uint, MarketSnapshot>()
                    : await universalis.GetMarketSnapshotsAsync(
                        config.DataCenterName,
                        candidateIds,
                        cancellationToken,
                        (processed, total) => UpdateScanProgressByPhase(processed, total, 40, 50));
                dcFetchSw.Stop();

                // Mark DC fetch completion at 50%
                UpdateScanProgressPercent(50);

                var evaluationSw = Stopwatch.StartNew();
                var itemsProcessedInEval = 0;
                var evalIncrementEvery = Math.Max(1, itemsToScan.Count / 50); // Distribute remaining 50% across eval
                var apiHealth = universalis.GetHealthSnapshot();

                foreach (var item in itemsToScan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var publishNow = false;

                    try
                    {
                        if (!homeCandidatesByItemId.TryGetValue(item.ItemId, out var homeCandidate))
                            continue;

                        if (!dcSnapshots.TryGetValue(item.ItemId, out var dcSnapshot))
                            continue;

                        var opportunity = BuildOpportunity(item, homeCandidate, dcSnapshot, apiHealth);
                        if (opportunity != null)
                        {
                            lock (resultsLock)
                                results.Add(opportunity);
                        }
                    }
                    finally
                    {
                        lock (publishCounterLock)
                        {
                            scannedSinceLastPublish++;
                            if (scannedSinceLastPublish >= PartialPublishBatchSize)
                            {
                                scannedSinceLastPublish = 0;
                                publishNow = true;
                            }
                        }

                        itemsProcessedInEval++;
                        if (itemsProcessedInEval % evalIncrementEvery == 0)
                            UpdateScanProgressByPhase(itemsProcessedInEval, itemsToScan.Count, 50, 100);

                        if (publishNow)
                        {
                            lock (resultsLock)
                                PublishPartialResults(results);
                        }
                    }
                }
                evaluationSw.Stop();

                sw.Stop();
                var sorted = results
                    .OrderByDescending(r => r.OwnedQuantity > 0)
                    .ThenByDescending(r => r.NetProfitPerUnit)
                    .ThenByDescending(r => r.SaleVelocityPerDay)
                    .ToList();

                database.SaveScanResults(config.WorldName, config.DataCenterName, sorted, sw.ElapsedMilliseconds);
                database.SaveLatestOpportunities(sorted);

                lock (cacheLock)
                {
                    lastResults = sorted;
                    scanProgressPercent = 100;
                    scanProcessedItems = scanTotalItems;
                    lastScanTiming = new ScanTimingSnapshot
                    {
                        HomeFetchMs = homeFetchSw.ElapsedMilliseconds,
                        HomeFilterMs = homeFilterSw.ElapsedMilliseconds,
                        DcFetchMs = dcFetchSw.ElapsedMilliseconds,
                        EvaluationMs = evaluationSw.ElapsedMilliseconds,
                        TotalMs = sw.ElapsedMilliseconds,
                        CandidateCount = candidateIds.Count
                    };
                }

                return sorted;
            }
            finally
            {
                lock (cacheLock)
                    scanInProgress = false;
            }
        }

        private void UpdateScanProgressByPhase(int phaseProcessed, int phaseTotal, int phaseStartPercent, int phaseEndPercent)
        {
            if (phaseTotal <= 0)
            {
                UpdateScanProgressPercent(phaseEndPercent);
                return;
            }

            var boundedProcessed = Math.Min(Math.Max(phaseProcessed, 0), phaseTotal);
            var phaseRatio = boundedProcessed / (double)phaseTotal;
            var phasePercent = phaseStartPercent + (int)Math.Round((phaseEndPercent - phaseStartPercent) * phaseRatio);
            UpdateScanProgressPercent(phasePercent);
        }

        private void UpdateScanProgressPercent(int percent)
        {
            lock (cacheLock)
            {
                var boundedPercent = Math.Min(Math.Max(percent, 0), 100);
                scanProgressPercent = Math.Max(scanProgressPercent, boundedPercent);

                if (scanTotalItems <= 0)
                {
                    scanProcessedItems = 0;
                    return;
                }

                if (scanProgressPercent >= 100)
                {
                    scanProcessedItems = scanTotalItems;
                    return;
                }

                var estimated = (int)Math.Ceiling((scanProgressPercent / 100.0) * scanTotalItems);
                scanProcessedItems = Math.Min(scanTotalItems, Math.Max(scanProcessedItems, Math.Max(0, estimated)));
            }
        }

        private bool TryBuildHomeSnapshotCandidate(
            MarketSnapshot home,
            ScanMode scanMode,
            out HomeSnapshotCandidate candidate)
        {
            candidate = new HomeSnapshotCandidate();

            // Always use the actual lowest active listing on Home World as the sell-side reference.
            var homeLowestListingPrice = home.Listings
                .Where(l => l.PricePerUnit > 0)
                .Select(l => l.PricePerUnit)
                .DefaultIfEmpty(home.LowestPrice)
                .Min();
            if (homeLowestListingPrice == 0)
                return false;

            var velocity = CalculateSaleVelocity(home.RecentSales, config.ScannerLookbackDays);
            var minVelocity = (scanMode == ScanMode.WeaponsOnly || scanMode == ScanMode.ArmorOnly || scanMode == ScanMode.AccessoriesOnly || scanMode == ScanMode.GearOnly)
                ? config.GearMinVelocityPerDay
                : config.MinSaleVelocityPerDay;

            if (velocity < minVelocity)
                return false;

            var (salesCount24h, unitsSold24h) = CalculateRecentSales24h(home.RecentSales);
            if (unitsSold24h < Math.Max(0, config.MinUnitsSold24h))
                return false;

            candidate = new HomeSnapshotCandidate
            {
                HomeLowestListingPrice = homeLowestListingPrice,
                VelocityPerDay = velocity,
                SalesCount24h = salesCount24h,
                UnitsSold24h = unitsSold24h,
                HomeListings = home.Listings
            };

            return true;
        }

        private ArbitrageOpportunity? BuildOpportunity(
            ItemLookup item,
            HomeSnapshotCandidate homeCandidate,
            MarketSnapshot dc,
            ApiHealthSnapshot apiHealth)
        {
            if (dc.LowestPrice == 0)
                return null;

            var sortedDcListings = dc.Listings
                .Where(l => l.PricePerUnit > 0)
                .OrderBy(l => l.PricePerUnit)
                .ToList();
            if (sortedDcListings.Count == 0)
                return null;

            var netSellPerUnit = homeCandidate.HomeLowestListingPrice * (1 - (config.MarketTaxRatePercent / 100.0));
            var buySelection = SelectBuyReference(sortedDcListings, netSellPerUnit);
            if (buySelection == null)
                return null;

            var buyPrice = buySelection.EffectiveBuyPricePerUnit;
            var buyWorld = buySelection.BuyWorld;

            var netProfit = netSellPerUnit - buyPrice;
            var profitPercent = buyPrice == 0 ? 0 : (netProfit / buyPrice) * 100;

            if (netProfit < config.MinNetProfitGil || profitPercent < config.MinNetProfitPercent)
                return null;

            var botPattern = DetectPotentialBotPattern(homeCandidate.HomeListings);
            var ownedQuantity = GetOwnedItemQuantity(item.ItemId);
            var freshnessMinutes = dc.MostRecentSaleUtc.HasValue
                ? Math.Max(0, (DateTime.UtcNow - dc.MostRecentSaleUtc.Value).TotalMinutes)
                : 999;
            var confidenceBreakdown = CalculateConfidenceBreakdown(homeCandidate, dc, profitPercent, freshnessMinutes, apiHealth);
            var confidence = confidenceBreakdown.Total;
            var lowTrust = confidence < 35 || freshnessMinutes > 180 || apiHealth.SafeZone == "Red";
            var trustReason = lowTrust
                ? BuildTrustReason(confidence, freshnessMinutes, apiHealth)
                : "Healthy";
            var travelPlan = BuildTravelPlanSummary(buyWorld, netProfit, homeCandidate.VelocityPerDay);
            var regime = DetectMarketRegime(homeCandidate, dc, freshnessMinutes);
            var maxByItemCapital = Math.Max(1, config.MaxCapitalPerItemGil) / Math.Max(1, (int)Math.Round(buyPrice));
            var recommendedQty = CalculateRecommendedBuyQty(homeCandidate.VelocityPerDay, confidence, buySelection.TargetQty, maxByItemCapital);
            var projectedBatchNet = netProfit * recommendedQty;
            var routeSummary = BuildRouteSummary(buyWorld);
            var explainability = BuildExplainabilitySummary(confidenceBreakdown, regime, travelPlan.WorthIt);
            
            // Calculate total quantity listed at home world's minimum price
            var homeWorldCurrentQty = (uint)homeCandidate.HomeListings
                .Where(l => l.PricePerUnit == homeCandidate.HomeLowestListingPrice)
                .Sum(l => (long)Math.Max(0u, l.Quantity));

            return new ArbitrageOpportunity
            {
                ItemId = item.ItemId,
                ItemName = item.Name,
                HomeWorldMinPrice = homeCandidate.HomeLowestListingPrice,
                DataCenterLowestPrice = (uint)Math.Max(0, Math.Round(buyPrice)),
                BuyFromWorld = buyWorld,
                NetProfitPerUnit = netProfit,
                ProfitPercent = profitPercent,
                SaleVelocityPerDay = homeCandidate.VelocityPerDay,
                SalesCount24h = homeCandidate.SalesCount24h,
                UnitsSold24h = homeCandidate.UnitsSold24h,
                HomeWorldCurrentQtyListing = homeWorldCurrentQty,
                PotentialBotSellerPattern = botPattern,
                SafeBuyQty = ArbitrageOpportunity.ComputeSafeBuyQty(homeCandidate.VelocityPerDay, botPattern),
                OwnedQuantity = ownedQuantity,
                ConfidenceScore = confidence,
                DataFreshnessMinutes = freshnessMinutes,
                IsLowTrust = lowTrust,
                TrustReason = trustReason,
                TravelPlanSummary = travelPlan.Summary,
                TravelWorthIt = travelPlan.WorthIt,
                RecommendedBuyQty = recommendedQty,
                MaxAffordableQtyByCapital = Math.Max(1, maxByItemCapital),
                ProjectedBatchNetGil = projectedBatchNet,
                RouteSummary = routeSummary,
                RiskRegime = regime,
                ExplainabilitySummary = explainability,
                ScoreVelocity = confidenceBreakdown.VelocityScore,
                ScoreSpread = confidenceBreakdown.SpreadScore,
                ScoreDepth = confidenceBreakdown.DepthScore,
                ScoreVolatility = confidenceBreakdown.VolatilityScore,
                ScoreFreshness = confidenceBreakdown.FreshnessScore,
                ScoreApiPenalty = confidenceBreakdown.ApiPenalty,
                NeedsManualReview = lowTrust || (config.EnableDegradedModeActionBlock && apiHealth.SafeZone != "Green"),
                ScannedUtc = DateTime.UtcNow
            };
        }

        private BuySelection? SelectBuyReference(IReadOnlyList<ListingRecord> sortedDcListings, double netSellPerUnit)
        {
            var cheapest = sortedDcListings[0];
            var cheapestPrice = cheapest.PricePerUnit;

            var cheapThreshold = Math.Max(1, config.CheapItemPriceThresholdGil);
            var minCheapQty = Math.Max(1, config.CheapItemMinProfitableQuantity);
            var isCheapItem = cheapestPrice <= cheapThreshold;

            if (!isCheapItem)
            {
                return new BuySelection
                {
                    EffectiveBuyPricePerUnit = cheapestPrice,
                    BuyWorld = cheapest.WorldName,
                    TargetQty = 1
                };
            }

            // For cheap items, avoid one-off bait listings by requiring enough profitable units
            // and using the blended buy price for that quantity.
            var maxProfitableBuyPrice = CalculateMaxProfitableBuyPrice(netSellPerUnit);
            if (maxProfitableBuyPrice <= 0)
                return null;

            var profitableListings = sortedDcListings
                .Where(l => l.PricePerUnit <= maxProfitableBuyPrice)
                .ToList();
            if (profitableListings.Count == 0)
                return null;

            var profitableUnits = profitableListings.Sum(l => (int)Math.Max(1u, l.Quantity));
            if (profitableUnits < minCheapQty)
                return null;

            var targetUnits = minCheapQty;
            var consumedUnits = 0;
            double totalCost = 0;
            var buyWorld = profitableListings[0].WorldName;

            foreach (var listing in profitableListings)
            {
                if (consumedUnits >= targetUnits)
                    break;

                var availableUnits = (int)Math.Max(1u, listing.Quantity);
                var takeUnits = Math.Min(availableUnits, targetUnits - consumedUnits);
                totalCost += takeUnits * listing.PricePerUnit;
                consumedUnits += takeUnits;
            }

            if (consumedUnits < targetUnits)
                return null;

            return new BuySelection
            {
                EffectiveBuyPricePerUnit = totalCost / consumedUnits,
                BuyWorld = buyWorld,
                TargetQty = targetUnits
            };
        }

        private double CalculateMaxProfitableBuyPrice(double netSellPerUnit)
        {
            if (netSellPerUnit <= 0)
                return 0;

            var maxByGil = netSellPerUnit - Math.Max(0, config.MinNetProfitGil);
            var denominator = 1.0 + (Math.Max(0, config.MinNetProfitPercent) / 100.0);
            var maxByPercent = denominator <= 0 ? 0 : netSellPerUnit / denominator;
            return Math.Floor(Math.Min(maxByGil, maxByPercent));
        }

        private sealed class BuySelection
        {
            public double EffectiveBuyPricePerUnit { get; init; }
            public string BuyWorld { get; init; } = string.Empty;
            public int TargetQty { get; init; } = 1;
        }

        private sealed class HomeSnapshotCandidate
        {
            public uint HomeLowestListingPrice { get; init; }
            public double VelocityPerDay { get; init; }
            public int SalesCount24h { get; init; }
            public int UnitsSold24h { get; init; }
            public IReadOnlyList<ListingRecord> HomeListings { get; init; } = Array.Empty<ListingRecord>();
        }

        private void PublishPartialResults(List<ArbitrageOpportunity> inProgressResults)
        {
            var partial = inProgressResults
                .OrderByDescending(r => r.OwnedQuantity > 0)
                .ThenByDescending(r => r.NetProfitPerUnit)
                .ThenByDescending(r => r.SaleVelocityPerDay)
                .ToList();

            lock (cacheLock)
                lastResults = partial;
        }

        public async Task<uint> FetchHomeFloorPriceAsync(uint itemId, CancellationToken cancellationToken)
        {
            var home = await universalis.GetMarketSnapshotAsync(config.WorldName, itemId, cancellationToken);
            return home?.LowestPrice ?? 0;
        }

        public async Task<MarketSnapshot?> FetchHomeSnapshotAsync(uint itemId, CancellationToken cancellationToken)
        {
            return await universalis.GetMarketSnapshotAsync(config.WorldName, itemId, cancellationToken);
        }

        public int GetHistoricalFillRatePercent(uint itemId, int days = 30)
        {
            var rows = database.GetTradeHistory(days)
                .Where(entry => entry.ItemId == itemId)
                .ToList();
            if (rows.Count == 0)
                return 50;

            var wins = rows.Count(entry => entry.SellPrice >= entry.BuyPrice);
            return (int)Math.Round((wins * 100.0) / rows.Count);
        }

        public double? GetAverageBuyPrice(uint itemId, int days = 60)
            => database.GetAverageBuyPrice(itemId, days);

        private int GetOwnedItemQuantity(uint itemId)
        {
            return retainerPriceService.GetOwnedItemQuantity(itemId);
        }

        private static string ResolveItemName(uint itemId)
        {
            try
            {
                var sheet = MarketAssistantPlugin.DataManager.GetExcelSheet<Item>();
                var row = sheet?.GetRow(itemId);
                var name = row?.Name.ToString() ?? string.Empty;
                return string.IsNullOrWhiteSpace(name) ? $"Item {itemId}" : name;
            }
            catch
            {
                return $"Item {itemId}";
            }
        }

        private static string CategorizeItem(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return "Unknown";

            var text = itemName.ToLowerInvariant();
            if (text.Contains("potion") || text.Contains("tincture") || text.Contains("elixir"))
                return "Potions";
            if (text.Contains("materia"))
                return "Materia";
            if (text.Contains("ore") || text.Contains("ingot") || text.Contains("lumber") || text.Contains("cloth"))
                return "Materials";
            if (text.Contains("ring") || text.Contains("bracelet") || text.Contains("earring") || text.Contains("necklace"))
                return "Accessories";
            if (text.Contains("sword") || text.Contains("axe") || text.Contains("bow") || text.Contains("gun") || text.Contains("staff"))
                return "Weapons";
            if (text.Contains("helm") || text.Contains("armor") || text.Contains("coat") || text.Contains("gloves") || text.Contains("boots"))
                return "Armor";
            if (text.Contains("food") || text.Contains("meal") || text.Contains("tea"))
                return "Food";
            return "Misc";
        }

        private static ConfidenceBreakdown CalculateConfidenceBreakdown(
            HomeSnapshotCandidate homeCandidate,
            MarketSnapshot dc,
            double profitPercent,
            double freshnessMinutes,
            ApiHealthSnapshot health)
        {
            var listings = dc.Listings.Where(listing => listing.PricePerUnit > 0).OrderBy(listing => listing.PricePerUnit).ToList();
            var spreadPercent = 0.0;
            if (listings.Count >= 2)
            {
                var low = listings[0].PricePerUnit;
                var second = listings[1].PricePerUnit;
                if (low > 0)
                    spreadPercent = ((second - low) / (double)low) * 100.0;
            }

            var totalQty = listings.Sum(listing => (double)Math.Max(1, listing.Quantity));
            var topQty = listings.Count == 0 ? 0 : Math.Max(1, listings[0].Quantity);
            var depthConcentration = totalQty <= 0 ? 1.0 : topQty / totalQty;

            var volatility = CalculateSalesVolatility(homeCandidate.HomeListings);
            var velocityScore = Math.Min(40, homeCandidate.VelocityPerDay * 8);
            var spreadScore = Math.Max(0, 20 - spreadPercent * 2);
            var depthScore = Math.Max(0, 15 - depthConcentration * 15);
            var volatilityScore = Math.Max(0, 15 - volatility * 10);
            var freshnessScore = Math.Max(0, 10 - (freshnessMinutes / 20.0));
            var apiPenalty = health.SafeZone == "Green" ? 0 : health.SafeZone == "Yellow" ? 5 : 15;
            var profitBonus = Math.Min(10, Math.Max(0, profitPercent / 5.0));

            var score = velocityScore + spreadScore + depthScore + volatilityScore + freshnessScore + profitBonus - apiPenalty;
            return new ConfidenceBreakdown
            {
                VelocityScore = velocityScore,
                SpreadScore = spreadScore,
                DepthScore = depthScore,
                VolatilityScore = volatilityScore,
                FreshnessScore = freshnessScore,
                ApiPenalty = apiPenalty,
                ProfitBonus = profitBonus,
                Total = Math.Clamp(score, 0, 100)
            };
        }

        private static double CalculateSalesVolatility(IReadOnlyList<ListingRecord> listings)
        {
            var prices = listings
                .Where(listing => listing.PricePerUnit > 0)
                .Select(listing => (double)listing.PricePerUnit)
                .ToList();
            if (prices.Count < 2)
                return 0;

            var mean = prices.Average();
            if (mean <= 0)
                return 0;

            var variance = prices.Sum(price => Math.Pow(price - mean, 2)) / prices.Count;
            var stdev = Math.Sqrt(variance);
            return stdev / mean;
        }

        private static string BuildTrustReason(double confidence, double freshnessMinutes, ApiHealthSnapshot health)
        {
            if (health.SafeZone == "Red")
                return "API health degraded";
            if (freshnessMinutes > 180)
                return "Stale market data";
            if (confidence < 35)
                return "Low confidence pattern";
            return "Healthy";
        }

        private static int CalculateRecommendedBuyQty(double velocityPerDay, double confidence, int targetQty, int maxByItemCapital)
        {
            var byVelocity = velocityPerDay < 1 ? 1 : velocityPerDay < 2.5 ? 2 : velocityPerDay < 4 ? 3 : 4;
            var byConfidence = confidence < 35 ? 1 : confidence < 55 ? 2 : confidence < 75 ? 3 : 4;
            var baseline = Math.Min(byVelocity, byConfidence);
            baseline = Math.Min(baseline, Math.Max(1, targetQty));
            return Math.Max(1, Math.Min(baseline, Math.Max(1, maxByItemCapital)));
        }

        private static string DetectMarketRegime(HomeSnapshotCandidate homeCandidate, MarketSnapshot dc, double freshnessMinutes)
        {
            if (freshnessMinutes > 180)
                return "Stale";

            var volatility = CalculateSalesVolatility(homeCandidate.HomeListings);
            var spread = 0.0;
            var ordered = dc.Listings.Where(l => l.PricePerUnit > 0).OrderBy(l => l.PricePerUnit).ToList();
            if (ordered.Count >= 2 && ordered[0].PricePerUnit > 0)
                spread = ((ordered[1].PricePerUnit - ordered[0].PricePerUnit) / (double)ordered[0].PricePerUnit) * 100.0;

            if (homeCandidate.VelocityPerDay > 5 && spread > 6)
                return "Patch Spike";
            if (volatility > 0.25)
                return "High Volatility";
            if (homeCandidate.VelocityPerDay < 1)
                return "Thinning Demand";
            return "Stable";
        }

        private string BuildRouteSummary(string buyWorld)
        {
            if (string.IsNullOrWhiteSpace(buyWorld) || string.Equals(buyWorld, config.WorldName, StringComparison.OrdinalIgnoreCase))
                return $"Stay on {config.WorldName}";
            return $"{config.WorldName} -> {buyWorld} -> {config.WorldName}";
        }

        private static string BuildExplainabilitySummary(ConfidenceBreakdown breakdown, string regime, bool travelWorthIt)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Regime {regime}; velocity {breakdown.VelocityScore:F1}, spread {breakdown.SpreadScore:F1}, depth {breakdown.DepthScore:F1}, volatility {breakdown.VolatilityScore:F1}, freshness {breakdown.FreshnessScore:F1}, api penalty {breakdown.ApiPenalty:F1}; travel {(travelWorthIt ? "worth" : "not worth")}.");
        }

        private (string Summary, bool WorthIt) BuildTravelPlanSummary(string buyWorld, double netProfitPerUnit, double velocityPerDay)
        {
            var qty = Math.Max(1, (int)Math.Round(Math.Min(8, Math.Max(1, velocityPerDay))));
            var travelCost = Math.Max(0, config.WorldTravelOverheadGil);
            var projected = (netProfitPerUnit * qty) - travelCost;
            var worthIt = projected >= Math.Max(0, config.WorldTravelMinNetGil);

            if (string.IsNullOrWhiteSpace(buyWorld) || string.Equals(buyWorld, config.WorldName, StringComparison.OrdinalIgnoreCase))
                return ("Local buy", true);

            return ($"{buyWorld} x{qty} | est net {projected:N0} after {travelCost:N0} travel", worthIt);
        }

        private sealed class ConfidenceBreakdown
        {
            public double VelocityScore { get; init; }
            public double SpreadScore { get; init; }
            public double DepthScore { get; init; }
            public double VolatilityScore { get; init; }
            public double FreshnessScore { get; init; }
            public double ApiPenalty { get; init; }
            public double ProfitBonus { get; init; }
            public double Total { get; init; }
        }

        private void EnsureItemCacheLoaded()
        {
            lock (cacheLock)
            {
                if (itemCache != null)
                    return;

                var loaded = new List<ItemLookup>(32000);
                var marketable = new List<ItemLookup>(24000);
                var gear = new List<ItemLookup>(12000);
                var weapons = new List<ItemLookup>(8000);
                var armor = new List<ItemLookup>(8000);
                var accessories = new List<ItemLookup>(5000);
                var consumables = new List<ItemLookup>(8000);
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
                            Name = name,
                            IsGear = row.EquipSlotCategory.RowId > 0,
                            RequiredLevel = (int)row.LevelEquip,
                            ItemLevel = (int)row.LevelItem.RowId
                        };

                        if (item.IsGear)
                        {
                            // Defensive correction in case upstream sheet mapping swaps these in a future API update.
                            if (item.RequiredLevel > 100 && item.ItemLevel > 0 && item.ItemLevel <= 100)
                            {
                                var correctedRequired = item.ItemLevel;
                                var correctedItemLevel = item.RequiredLevel;
                                item = new ItemLookup
                                {
                                    ItemId = item.ItemId,
                                    Name = item.Name,
                                    IsGear = true,
                                    RequiredLevel = correctedRequired,
                                    ItemLevel = correctedItemLevel
                                };
                            }
                        }

                        loaded.Add(item);

                        if (!row.IsUntradable)
                        {
                            marketable.Add(item);

                            if (IsGearItem(row))
                            {
                                gear.Add(item);

                                var equipmentType = ClassifyEquipmentType(row.EquipSlotCategory.RowId);
                                if (equipmentType == EquipmentType.Weapon)
                                    weapons.Add(item);
                                else if (equipmentType == EquipmentType.Armor)
                                    armor.Add(item);
                                else if (equipmentType == EquipmentType.Accessory)
                                    accessories.Add(item);
                            }
                            else
                            {
                                // Not gear = consumable (potions, food, materials, etc.)
                                consumables.Add(item);
                            }
                        }
                    }
                }

                itemCache = loaded;
                marketableItemCache = marketable
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                gearItemCache = gear
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                weaponItemCache = weapons
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                armorItemCache = armor
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                accessoryItemCache = accessories
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                consumableItemCache = consumables
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                LoggingService.LogInfo($"Loaded {loaded.Count} items into search cache");
                LoggingService.LogInfo($"Prepared {marketableItemCache.Count} marketable scan candidates");
                LoggingService.LogInfo($"Prepared {gearItemCache.Count} gear scan candidates");
                LoggingService.LogInfo($"Prepared {weaponItemCache.Count} weapon scan candidates");
                LoggingService.LogInfo($"Prepared {armorItemCache.Count} armor scan candidates");
                LoggingService.LogInfo($"Prepared {accessoryItemCache.Count} accessory scan candidates");
                LoggingService.LogInfo($"Prepared {consumableItemCache.Count} consumable scan candidates");
            }
        }

        private static bool IsGearItem(Item item)
        {
            // EquipSlotCategory > 0 captures equippable items (weapons, armor, accessories).
            return item.EquipSlotCategory.RowId > 0;
        }

        private enum EquipmentType
        {
            Unknown,
            Weapon,
            Armor,
            Accessory
        }

        private static EquipmentType ClassifyEquipmentType(uint equipSlotCategoryId)
        {
            // Equip slot category IDs are stable enough for broad scanner segmentation:
            // 1-2 and 13-14: weapon/offhand sets, 3-8: armor slots, 9-12: accessories.
            return equipSlotCategoryId switch
            {
                1 or 2 or 13 or 14 => EquipmentType.Weapon,
                >= 3 and <= 8 => EquipmentType.Armor,
                >= 9 and <= 12 => EquipmentType.Accessory,
                _ => EquipmentType.Unknown
            };
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

        private static (int SalesCount, int UnitsSold) CalculateRecentSales24h(IReadOnlyList<SaleRecord> sales)
        {
            if (sales.Count == 0)
                return (0, 0);

            var cutoff = DateTime.UtcNow.AddHours(-24);
            var relevant = sales.Where(s => s.TimestampUtc >= cutoff).ToList();
            if (relevant.Count == 0)
                return (0, 0);

            var units = relevant.Sum(s => (int)Math.Max(1u, s.Quantity));
            return (relevant.Count, units);
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

        private static double ComputeAllocationScore(ArbitrageOpportunity opportunity)
        {
            var trustPenalty = opportunity.IsLowTrust ? 0.55 : 1.0;
            var regimePenalty = opportunity.RiskRegime == "Patch Spike" ? 0.80 : opportunity.RiskRegime == "High Volatility" ? 0.88 : 1.0;
            var velocityWeight = Math.Clamp(opportunity.SaleVelocityPerDay / 4.0, 0.4, 1.3);
            var profitWeight = Math.Clamp(opportunity.ProfitPercent / 20.0, 0.35, 1.4);
            return opportunity.ConfidenceScore * velocityWeight * profitWeight * trustPenalty * regimePenalty;
        }

        private string EvaluateExclusionReason(
            WatchedItem watched,
            IReadOnlyDictionary<uint, MarketSnapshot> homeSnapshots,
            IReadOnlyDictionary<uint, MarketSnapshot> dcSnapshots,
            ApiHealthSnapshot health)
        {
            if (!homeSnapshots.TryGetValue(watched.ItemId, out var home))
                return "No home-world market data returned";

            if (!TryBuildHomeSnapshotCandidate(home, currentScanMode, out var candidate))
            {
                var velocity = CalculateSaleVelocity(home.RecentSales, config.ScannerLookbackDays);
                if (velocity < config.MinSaleVelocityPerDay)
                    return $"Filtered: velocity {velocity:F2}/day below threshold {config.MinSaleVelocityPerDay:F2}";

                var (_, units24h) = CalculateRecentSales24h(home.RecentSales);
                if (units24h < config.MinUnitsSold24h)
                    return $"Filtered: units sold {units24h} below threshold {config.MinUnitsSold24h}";

                if (home.LowestPrice == 0)
                    return "Filtered: no active home listings";

                return "Filtered by home-world quality rules";
            }

            if (!dcSnapshots.TryGetValue(watched.ItemId, out var dc))
                return "No data-center market data returned";

            if (dc.LowestPrice == 0 || dc.Listings.Count == 0)
                return "Filtered: no buy-side listings in data center";

            var netSellPerUnit = candidate.HomeLowestListingPrice * (1 - (config.MarketTaxRatePercent / 100.0));
            var buySelection = SelectBuyReference(dc.Listings.Where(l => l.PricePerUnit > 0).OrderBy(l => l.PricePerUnit).ToList(), netSellPerUnit);
            if (buySelection == null)
                return "Filtered: cheap-listing depth trap or no profitable buy depth";

            var buyPrice = buySelection.EffectiveBuyPricePerUnit;
            var netProfit = netSellPerUnit - buyPrice;
            var profitPercent = buyPrice <= 0 ? 0 : (netProfit / buyPrice) * 100.0;
            if (netProfit < config.MinNetProfitGil)
                return $"Filtered: net/unit {netProfit:N0} below gil minimum {config.MinNetProfitGil:N0}";
            if (profitPercent < config.MinNetProfitPercent)
                return $"Filtered: margin {profitPercent:F1}% below minimum {config.MinNetProfitPercent:F1}%";

            if (config.EnableDegradedModeActionBlock && health.SafeZone != "Green")
                return $"Deferred: API safe zone is {health.SafeZone}";

            return string.Empty;
        }
    }
}
