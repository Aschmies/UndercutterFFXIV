using System;
using System.Collections.Generic;
using System.Linq;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Analyzes market data to identify flipping opportunities
    /// </summary>
    public class FlipAnalyzerService
    {
        private MarketTracker tracker { get; }
        private List<FlipOpportunity> opportunities = new();

        public FlipAnalyzerService(MarketTracker tracker)
        {
            this.tracker = tracker;
        }

        public List<FlipOpportunity> GetAllOpportunities()
        {
            return new List<FlipOpportunity>(opportunities);
        }

        public List<FlipOpportunity> AnalyzeForFlips(int minProfitGil = 100, double minProfitPercent = 5.0,
            IEnumerable<(uint ItemId, string ItemName)>? extraItems = null)
        {
            opportunities.Clear();

            // Analyze items already listed on the market board
            foreach (var item in tracker.GetListedItems())
            {
                var opportunity = AnalyzeItem(item, minProfitGil, minProfitPercent);
                if (opportunity != null)
                    opportunities.Add(opportunity);
            }

            // Also analyze watchlist/extra items using price history only (no board visit needed)
            if (extraItems != null)
            {
                var listedIds = tracker.GetListedItems().Select(i => i.ItemId).ToHashSet();
                foreach (var (itemId, itemName) in extraItems)
                {
                    if (listedIds.Contains(itemId)) continue; // already covered above
                    var opportunity = AnalyzeByHistory(itemId, itemName, minProfitGil, minProfitPercent);
                    if (opportunity != null)
                        opportunities.Add(opportunity);
                }
            }

            return opportunities.OrderByDescending(o => o.ProfitPotential).ToList();
        }

        private FlipOpportunity? AnalyzeItem(ListedItem item, int minProfitGil, double minProfitPercent)
        {
            var opp = AnalyzeByHistory(item.ItemId, item.ItemName, minProfitGil, minProfitPercent);
            // Prefer the live CurrentLowestPrice from the tracked listing over the last history snapshot
            if (opp != null && item.CurrentLowestPrice > 0)
                opp.CurrentPrice = item.CurrentLowestPrice;
            return opp;
        }

        private FlipOpportunity? AnalyzeByHistory(uint itemId, string itemName, int minProfitGil, double minProfitPercent)
        {
            var history = tracker.GetPriceHistory(itemId, 90);
            if (history.Count < 2)
                return null;

            var buyPrice = history.Min(h => h.LowestPrice);
            var sellPrice = history.Max(h => h.LowestPrice);
            var avgPrice = (uint)history.Average(h => h.AveragePrice);
            var profit = sellPrice > buyPrice ? sellPrice - buyPrice : 0;
            var profitPercent = buyPrice > 0 ? (profit * 100.0) / buyPrice : 0;

            if (profit < minProfitGil || profitPercent < minProfitPercent)
                return null;

            var volatility = avgPrice > 0 ? (sellPrice - buyPrice) / (double)avgPrice : 0;
            var trend = CalculateTrend(history);

            return new FlipOpportunity
            {
                ItemId = itemId,
                ItemName = itemName,
                BuyPrice = buyPrice,
                SellPrice = sellPrice,
                CurrentPrice = history.Last().LowestPrice,
                AveragePrice = avgPrice,
                Volatility = volatility,
                Trend = trend,
                SamplesCount = history.Count
            };
        }

        public List<FlipOpportunity> FindBuyingOpportunities()
        {
            return opportunities
                .Where(o => o.CurrentPrice > 0 && o.AveragePrice > 0 && 
                           o.CurrentPrice <= o.AveragePrice * 0.95)  // 5% below average
                .OrderBy(o => o.CurrentPrice)
                .ToList();
        }

        public List<FlipOpportunity> FindSellingOpportunities()
        {
            return opportunities
                .Where(o => o.CurrentPrice > 0 && o.AveragePrice > 0 &&
                           o.CurrentPrice >= o.AveragePrice * 1.05)  // 5% above average
                .OrderByDescending(o => o.CurrentPrice)
                .ToList();
        }

        private string CalculateTrend(List<MarketPriceSnapshot> prices)
        {
            if (prices.Count < 2)
                return "Stable";

            var first = prices.First().LowestPrice;
            var last = prices.Last().LowestPrice;

            if (last > first * 1.05)
                return "Rising";
            else if (last < first * 0.95)
                return "Falling";
            else
                return "Stable";
        }
    }
}
