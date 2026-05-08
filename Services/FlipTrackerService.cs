using System;
using System.Collections.Generic;
using System.Linq;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Tracks completed flips and maintains watchlist
    /// </summary>
    public class FlipTrackerService
    {
        private List<FlipTransaction> transactions = new();
        private List<WatchlistItem> watchlist = new();
        private PersistenceService persistence { get; set; }

        public void InitializePersistence(PersistenceService persistenceService)
        {
            persistence = persistenceService;
            LoadTransactions();
            LoadWatchlist();
        }

        public void RecordFlip(uint itemId, string name, uint buyPrice, uint sellPrice, uint qty, double taxPercent = 5.0)
        {
            var transaction = new FlipTransaction
            {
                ItemId = itemId,
                ItemName = name,
                BuyPrice = buyPrice,
                SellPrice = sellPrice,
                Quantity = qty,
                BuyTime = DateTime.Now,
                SellTime = DateTime.Now,
                TaxPercentage = taxPercent
            };

            transactions.Add(transaction);
            LoggingService.LogInfo($"Recorded flip: {name} - Bought: {buyPrice}, Sold: {sellPrice}, Profit: {transaction.NetProfit}");
            SaveTransactions();
        }

        public FlipStatistics GetStatistics(int days = 30)
        {
            var cutoff = DateTime.Now.AddDays(-days);
            var relevant = transactions.Where(t => t.SellTime >= cutoff).ToList();

            if (relevant.Count == 0)
                return new FlipStatistics();

            var totalProfit = (ulong)relevant.Sum(t => (long)t.NetProfit);
            var totalVolume = (ulong)relevant.Sum(t => (long)t.SellRevenue);
            var avgPercent = relevant.Count > 0 ? relevant.Average(t => (t.SellPrice > t.BuyPrice ? ((t.SellPrice - t.BuyPrice) * 100.0 / t.BuyPrice) : 0)) : 0;
            var avgHours = relevant.Count > 0 ? relevant.Average(t => t.HoldingTime) : 0;

            var itemGroups = relevant.GroupBy(t => t.ItemId).OrderByDescending(g => g.Sum(t => (long)t.NetProfit));
            var topItem = itemGroups.FirstOrDefault();

            return new FlipStatistics
            {
                TotalFlips = relevant.Count,
                TotalProfit = totalProfit,
                TotalVolume = totalVolume,
                AverageProfitPercentage = avgPercent,
                AverageHoldingHours = avgHours,
                MostFlippedItemId = topItem?.Key ?? 0,
                MostFlippedItemName = topItem?.FirstOrDefault()?.ItemName ?? "",
                HighestSingleProfit = (ulong)relevant.Max(t => (long)t.NetProfit),
                PeriodStart = cutoff,
                PeriodEnd = DateTime.Now
            };
        }

        public void AddToWatchlist(uint itemId, string name, uint targetBuyPrice, uint targetSellPrice, string notes = "")
        {
            var existing = watchlist.FirstOrDefault(w => w.ItemId == itemId);
            if (existing != null)
            {
                existing.TargetBuyPrice = targetBuyPrice;
                existing.TargetSellPrice = targetSellPrice;
                existing.Notes = notes;
            }
            else
            {
                watchlist.Add(new WatchlistItem
                {
                    ItemId = itemId,
                    ItemName = name,
                    TargetBuyPrice = targetBuyPrice,
                    TargetSellPrice = targetSellPrice,
                    Notes = notes
                });
            }
            SaveWatchlist();
        }

        public void RemoveFromWatchlist(uint itemId)
        {
            watchlist.RemoveAll(w => w.ItemId == itemId);
            SaveWatchlist();
        }

        public List<WatchlistItem> GetWatchlist() => watchlist.Where(w => w.Active).ToList();

        public List<WatchlistItem> GetWatchlistAlerts(MarketTracker tracker)
        {
            var alerts = new List<WatchlistItem>();
            foreach (var item in watchlist.Where(w => w.Active && w.TargetBuyPrice > 0))
            {
                // Use price history so watchlist-only items (never visited at board) still get alerts
                var history = tracker.GetPriceHistory(item.ItemId, 1);
                if (history.Count > 0 && history.Last().LowestPrice <= item.TargetBuyPrice)
                    alerts.Add(item);
            }
            return alerts;
        }

        public List<FlipTransaction> GetRecentTransactions(int count = 10)
        {
            return transactions.OrderByDescending(t => t.SellTime).Take(count).ToList();
        }

        public List<FlipTransaction> GetTransactions(int days = 30)
        {
            var cutoff = DateTime.Now.AddDays(-days);
            return transactions.Where(t => t.SellTime >= cutoff).OrderByDescending(t => t.SellTime).ToList();
        }

        public Dictionary<uint, (int count, ulong profit, double margin)> GetPerItemStats(int days = 30)
        {
            var cutoff = DateTime.Now.AddDays(-days);
            var relevant = transactions.Where(t => t.SellTime >= cutoff).ToList();

            var result = new Dictionary<uint, (int, ulong, double)>();
            foreach (var group in relevant.GroupBy(t => t.ItemId))
            {
                var count = group.Count();
                var profit = (ulong)group.Sum(t => (long)t.NetProfit);
                var avgMargin = group.Average(t => (t.SellPrice > t.BuyPrice ? ((t.SellPrice - t.BuyPrice) * 100.0 / t.BuyPrice) : 0));
                result[group.Key] = (count, profit, avgMargin);
            }
            return result;
        }

        private void SaveTransactions()
        {
            if (persistence != null)
                persistence.SaveFlipTransactions(transactions);
        }

        private void LoadTransactions()
        {
            if (persistence != null)
                transactions = persistence.LoadFlipTransactions();
        }

        private void SaveWatchlist()
        {
            if (persistence != null)
                persistence.SaveWatchlist(watchlist);
        }

        private void LoadWatchlist()
        {
            if (persistence != null)
                watchlist = persistence.LoadWatchlist();
        }
    }
}
