using System;
using System.Collections.Generic;
using System.Linq;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Tracks active flips and suggests when to sell based on profit targets
    /// </summary>
    public class SellSuggestionService
    {
        private List<ActiveFlip> activeFlips = new();
        private MarketTracker tracker { get; }
        private readonly object _lock = new();

        public class ActiveFlip
        {
            public uint ItemId { get; set; }
            public string ItemName { get; set; } = "";
            public uint BuyPrice { get; set; }
            public uint TargetSellPrice { get; set; }
            public uint MinProfitPerUnit { get; set; }
            public uint Quantity { get; set; }
            public DateTime BuyTime { get; set; } = DateTime.Now;
            public uint CurrentLowestPrice { get; set; }
            public bool IsReadyToSell { get; set; }
            public string Reason { get; set; } = "";

            // Computed
            public uint PotentialProfit => CurrentLowestPrice > BuyPrice 
                ? (CurrentLowestPrice - BuyPrice) * Quantity 
                : 0;
            public double ProfitPercent => BuyPrice > 0 
                ? ((CurrentLowestPrice - BuyPrice) * 100.0) / BuyPrice 
                : 0;
            public double HoldingHours => (DateTime.Now - BuyTime).TotalHours;
        }

        public SellSuggestionService(MarketTracker tracker)
        {
            this.tracker = tracker;
        }

        /// <summary>
        /// Record that you bought an item (start active flip)
        /// </summary>
        public void StartFlip(uint itemId, string itemName, uint buyPrice, uint qty, uint targetSellPrice, uint minProfitPerUnit = 0)
        {
            lock (_lock)
            {
                var existing = activeFlips.FirstOrDefault(f => f.ItemId == itemId);

                if (existing != null)
                {
                    existing.Quantity += qty;
                    existing.BuyPrice = buyPrice;
                    existing.TargetSellPrice = targetSellPrice;
                    existing.MinProfitPerUnit = minProfitPerUnit;
                }
                else
                {
                    activeFlips.Add(new ActiveFlip
                    {
                        ItemId = itemId,
                        ItemName = itemName,
                        BuyPrice = buyPrice,
                        TargetSellPrice = targetSellPrice,
                        MinProfitPerUnit = minProfitPerUnit > 0 ? minProfitPerUnit : (uint)(buyPrice * 0.1),
                        Quantity = qty,
                        BuyTime = DateTime.Now
                    });
                }
            }

            LoggingService.LogInfo($"Started flip: {itemName} x{qty} @ {buyPrice} gil");
        }

        /// <summary>
        /// Update current market prices for all active flips
        /// </summary>
        public void UpdateMarketPrices()
        {
            List<ActiveFlip> snapshot;
            lock (_lock) { snapshot = activeFlips.ToList(); }

            foreach (var flip in snapshot)
            {
                var history = tracker.GetPriceHistory(flip.ItemId, 1);
                if (history.Count > 0)
                {
                    flip.CurrentLowestPrice = history.Last().LowestPrice;
                    EvaluateSellSignal(flip);
                }
            }
        }

        /// <summary>
        /// Check if item should be sold based on profit targets
        /// </summary>
        private void EvaluateSellSignal(ActiveFlip flip)
        {
            flip.IsReadyToSell = false;
            flip.Reason = "";

            if (flip.CurrentLowestPrice == 0 || flip.CurrentLowestPrice <= flip.BuyPrice)
            {
                flip.Reason = "Price below buy price - wait longer";
                return;
            }

            var profitPerUnit = flip.CurrentLowestPrice - flip.BuyPrice;

            // Signal 1: Hit target price
            if (flip.CurrentLowestPrice >= flip.TargetSellPrice)
            {
                flip.IsReadyToSell = true;
                flip.Reason = $"🎯 Target price reached! ({flip.TargetSellPrice} gil)";
                return;
            }

            // Signal 2: Hit minimum profit
            if (profitPerUnit >= flip.MinProfitPerUnit)
            {
                flip.IsReadyToSell = true;
                flip.Reason = $"✓ Profit target met ({profitPerUnit} gil/unit, {flip.ProfitPercent:F1}%)";
                return;
            }

            // Signal 3: Price trending down after good profit
            var trend = CalculateTrend(flip.ItemId);
            if (profitPerUnit >= flip.MinProfitPerUnit * 0.8 && trend == "Falling")
            {
                flip.IsReadyToSell = true;
                flip.Reason = "⚠️ Price falling - sell now before it drops more!";
                return;
            }

            // Signal 4: Been holding too long
            if (flip.HoldingHours > 24 && profitPerUnit >= flip.MinProfitPerUnit * 0.5)
            {
                flip.IsReadyToSell = true;
                flip.Reason = $"⏰ Been holding {flip.HoldingHours:F0}h - take the {flip.ProfitPercent:F1}% profit";
                return;
            }

            flip.Reason = $"Waiting: {profitPerUnit} gil/unit ({flip.ProfitPercent:F1}%), need {flip.MinProfitPerUnit}";
        }

        private string CalculateTrend(uint itemId)
        {
            var history = tracker.GetPriceHistory(itemId, 7);
            if (history.Count < 2)
                return "Stable";

            var first = history.First().LowestPrice;
            var last = history.Last().LowestPrice;

            if (last > first * 1.05)
                return "Rising";
            else if (last < first * 0.95)
                return "Falling";
            else
                return "Stable";
        }

        /// <summary>
        /// Get all active flips with sell signals
        /// </summary>
        public List<ActiveFlip> GetActiveFlips()
        {
            lock (_lock) { return new List<ActiveFlip>(activeFlips); }
        }

        /// <summary>
        /// Get only items ready to sell
        /// </summary>
        public List<ActiveFlip> GetSellReadyItems()
        {
            lock (_lock)
                return activeFlips.Where(f => f.IsReadyToSell).OrderByDescending(f => f.PotentialProfit).ToList();
        }

        /// <summary>
        /// Get items close to sell threshold (90%+ of target)
        /// </summary>
        public List<ActiveFlip> GetNearReadyItems()
        {
            lock (_lock)
                return activeFlips
                    .Where(f => !f.IsReadyToSell &&
                               f.CurrentLowestPrice >= f.TargetSellPrice * 0.9)
                    .OrderByDescending(f => f.CurrentLowestPrice)
                    .ToList();
        }

        /// <summary>
        /// Complete a flip and remove from active tracking
        /// </summary>
        public void CompleteFlip(uint itemId)
        {
            ActiveFlip? flip;
            lock (_lock)
            {
                flip = activeFlips.FirstOrDefault(f => f.ItemId == itemId);
                if (flip != null) activeFlips.Remove(flip);
            }
            if (flip != null)
                LoggingService.LogInfo($"Completed flip: {flip.ItemName} - Profit: {flip.PotentialProfit} gil");
        }

        /// <summary>
        /// Cancel active flip
        /// </summary>
        public void CancelFlip(uint itemId)
        {
            lock (_lock) { activeFlips.RemoveAll(f => f.ItemId == itemId); }
            LoggingService.LogInfo($"Cancelled flip for item {itemId}");
        }

        /// <summary>
        /// Get summary statistics for active flips
        /// </summary>
        public (int count, ulong totalPotentialProfit, double avgProfitPercent, int readyToSell) GetActiveSummary()
        {
            List<ActiveFlip> snapshot;
            lock (_lock) { snapshot = activeFlips.ToList(); }

            var readyCount = snapshot.Count(f => f.IsReadyToSell);
            var totalProfit = (ulong)snapshot.Sum(f => (long)f.PotentialProfit);
            var avgPercent = snapshot.Count > 0 ? snapshot.Average(f => f.ProfitPercent) : 0;
            return (snapshot.Count, totalProfit, avgPercent, readyCount);
        }
    }
}
