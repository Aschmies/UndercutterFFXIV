using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Central service for tracking market prices, managing items, and calculating profits
    /// </summary>
    public class MarketTracker
    {
        private Dictionary<uint, ListedItem> trackedItems = new();
        private Dictionary<uint, List<MarketPriceSnapshot>> priceHistory = new();
        private List<PriceAlert> alerts = new();
        private uint undercutAmount = 1;
        private PersistenceService persistence { get; set; }
        private UniversalisApiService universalisApi { get; set; }
        private readonly object _lock = new();
        private const int MaxSnapshotsPerItem = 500;

        public MarketTracker(uint undercutAmount = 1)
        {
            this.undercutAmount = undercutAmount;
            universalisApi = new UniversalisApiService();
        }

        public void InitializePersistence(PersistenceService persistenceService)
        {
            persistence = persistenceService;
            LoadFromPersistence();
        }

        public void UpdateListedItem(uint itemId, string name, uint qty, uint price, uint craftingCost = 0)
        {
            var item = new ListedItem
            {
                ItemId = itemId,
                ItemName = name,
                Quantity = qty,
                ListedPrice = price,
                CraftingCost = craftingCost,
                LastUpdated = DateTime.Now
            };

            lock (_lock) { trackedItems[itemId] = item; }
            LoggingService.LogInfo($"Updated tracked item: {name} (ID: {itemId}) at {price} gil");
            SaveToPersistence();
        }

        public void UpdateMarketPrice(uint itemId, string name, uint lowestPrice, uint medianPrice, uint averagePrice, uint quantityListed)
        {
            var snapshot = new MarketPriceSnapshot
            {
                ItemId = itemId,
                ItemName = name,
                LowestPrice = lowestPrice,
                MedianPrice = medianPrice,
                AveragePrice = averagePrice,
                QuantityListed = quantityListed,
                Timestamp = DateTime.Now
            };

            lock (_lock)
            {
                if (!priceHistory.ContainsKey(itemId))
                    priceHistory[itemId] = new List<MarketPriceSnapshot>();

                var list = priceHistory[itemId];
                list.Add(snapshot);
                // Cap per-item history to avoid unbounded memory/disk growth
                if (list.Count > MaxSnapshotsPerItem)
                    list.RemoveAt(0);
            }

            CheckForUndercutting(itemId, name, lowestPrice);
            LoggingService.LogInfo($"Updated market price for {name}: {lowestPrice} gil (qty: {quantityListed})");
            // Caller (RefreshPricesFromUniversalis) calls SaveToPersistence() once after the loop
        }

        private void CheckForUndercutting(uint itemId, string name, uint marketLowestPrice)
        {
            lock (_lock)
            {
                if (!trackedItems.ContainsKey(itemId)) return;

                var listedItem = trackedItems[itemId];
                if (marketLowestPrice < listedItem.ListedPrice &&
                    listedItem.ListedPrice - marketLowestPrice >= undercutAmount)
                {
                    alerts.Add(new PriceAlert
                    {
                        ItemId = itemId,
                        ItemName = name,
                        OldPrice = listedItem.ListedPrice,
                        NewPrice = marketLowestPrice,
                        Timestamp = DateTime.Now,
                        Acknowledged = false
                    });
                    LoggingService.LogWarning($"UNDERCUT: {name} - Your {listedItem.ListedPrice} → Market {marketLowestPrice} (diff: {listedItem.ListedPrice - marketLowestPrice})");
                }
            }
        }

        public List<ListedItem> GetListedItems()
        {
            lock (_lock) { return trackedItems.Values.ToList(); }
        }

        public List<MarketPriceSnapshot> GetPriceHistory(uint itemId, int days = 90)
        {
            lock (_lock)
            {
                if (!priceHistory.ContainsKey(itemId))
                    return new List<MarketPriceSnapshot>();

                var cutoff = DateTime.Now.AddDays(-days);
                return priceHistory[itemId].Where(p => p.Timestamp >= cutoff).ToList();
            }
        }

        public PriceTrend CalculateTrend(List<MarketPriceSnapshot> prices)
        {
            if (prices.Count < 2)
                return new PriceTrend { TrendDirection = "Stable", SampleCount = prices.Count };

            var recent = prices.TakeLast(3).ToList();
            if (recent.Count < 2)
                return new PriceTrend { TrendDirection = "Stable", SampleCount = prices.Count };

            var avgRecent = recent.Average(p => p.LowestPrice);
            var avgOlder = prices.Take(Math.Max(3, prices.Count - 3)).Average(p => p.LowestPrice);

            var direction = avgRecent > avgOlder * 1.05 ? "Rising" :
                            avgRecent < avgOlder * 0.95 ? "Falling" : "Stable";

            return new PriceTrend
            {
                TrendDirection = direction,
                SampleCount = prices.Count,
                LowPrice = (uint)prices.Min(p => p.LowestPrice),
                HighPrice = (uint)prices.Max(p => p.LowestPrice),
                AveragePrice = (uint)prices.Average(p => p.LowestPrice),
                StartTime = prices.First().Timestamp,
                EndTime = prices.Last().Timestamp
            };
        }

        public List<PriceAlert> GetAlerts()
        {
            return alerts;
        }

        public List<PriceAlert> GetUnacknowledgedAlerts()
        {
            return alerts.Where(a => !a.Acknowledged).ToList();
        }

        public void AcknowledgeAlert(int index)
        {
            if (index >= 0 && index < alerts.Count)
            {
                alerts[index].Acknowledged = true;
                SaveToPersistence();
            }
        }

        public void ClearOldAlerts(int daysToKeep = 7)
        {
            var cutoff = DateTime.Now.AddDays(-daysToKeep);
            alerts.RemoveAll(a => a.Timestamp < cutoff);
            SaveToPersistence();
        }

        public void SaveToPersistence()
        {
            if (persistence == null) return;
            List<ListedItem> itemsSnapshot;
            Dictionary<uint, List<MarketPriceSnapshot>> historySnapshot;
            List<PriceAlert> alertsSnapshot;
            lock (_lock)
            {
                itemsSnapshot = trackedItems.Values.ToList();
                historySnapshot = priceHistory.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
                alertsSnapshot = alerts.ToList();
            }
            persistence.SaveTrackedItems(itemsSnapshot);
            persistence.SavePriceHistory(historySnapshot);
            persistence.SaveAlerts(alertsSnapshot);
        }

        public void LoadFromPersistence()
        {
            if (persistence != null)
            {
                var loaded = persistence.LoadTrackedItems();
                trackedItems = loaded.ToDictionary(x => x.ItemId);

                var history = persistence.LoadPriceHistory();
                priceHistory = history;

                alerts = persistence.LoadAlerts();
                LoggingService.LogInfo("Market data loaded from persistence");
            }
        }

        /// <summary>
        /// Fetch current market prices from Universalis for all tracked items plus any extra (watchlist) items
        /// </summary>
        public async Task RefreshPricesFromUniversalis(string worldName, IEnumerable<(uint ItemId, string ItemName)>? extraItems = null)
        {
            // Merge listed items + extra items, deduplicating by item ID
            var listedItems = GetListedItems().Select(i => (i.ItemId, i.ItemName));
            var toFetch = listedItems
                .Concat(extraItems ?? Enumerable.Empty<(uint, string)>())
                .GroupBy(t => t.ItemId)
                .Select(g => g.First())
                .ToList();

            var successCount = 0;

            foreach (var (itemId, itemName) in toFetch)
            {
                try
                {
                    var data = await universalisApi.GetPriceData(worldName, itemId, itemName);

                    if (data != null && data.LowestPrice > 0)
                    {
                        UpdateMarketPrice(
                            data.ItemId,
                            data.ItemName,
                            data.LowestPrice,
                            data.MedianPrice,
                            data.AveragePrice,
                            data.QuantityListed);

                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Failed to fetch {itemName}: {ex.Message}");
                }
            }

            // Single save after the entire loop (not once per item)
            SaveToPersistence();
            LoggingService.LogInfo($"✓ Universalis sync complete: Updated {successCount}/{toFetch.Count} items");
        }

        /// <summary>
        /// Fetch a single item from Universalis (used by SearchWindow for on-demand lookups)
        /// </summary>
        public async Task<bool> FetchItemFromUniversalis(string worldName, uint itemId, string itemName)
        {
            try
            {
                var data = await universalisApi.GetPriceData(worldName, itemId, itemName);
                if (data != null && data.LowestPrice > 0)
                {
                    UpdateMarketPrice(data.ItemId, data.ItemName, data.LowestPrice,
                        data.MedianPrice, data.AveragePrice, data.QuantityListed);
                    SaveToPersistence();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"FetchItemFromUniversalis failed for {itemName}: {ex.Message}");
                return false;
            }
        }
    }
}
