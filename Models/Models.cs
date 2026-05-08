using System;
using System.Collections.Generic;

namespace UndercutterFFXIV.Models
{
    /// <summary>
    /// Represents an item currently listed for sale by the player
    /// </summary>
    public class ListedItem
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public uint Quantity { get; set; }
        public uint ListedPrice { get; set; }
        public uint CurrentLowestPrice { get; set; }
        public uint CraftingCost { get; set; }
        public double TaxPercentage { get; set; }
        public bool IsFavorite { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        public uint ProfitPerUnit => ListedPrice > CraftingCost ? ListedPrice - CraftingCost : 0;
        public uint ProfitAfterTax
        {
            get
            {
                var profit = ProfitPerUnit * (uint)(100 - TaxPercentage) / 100;
                return profit;
            }
        }
        public ulong TotalProfit => (ulong)ProfitAfterTax * Quantity;
    }

    /// <summary>
    /// Historical market price data for an item
    /// </summary>
    public class MarketPriceSnapshot
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public uint LowestPrice { get; set; }
        public uint MedianPrice { get; set; }
        public uint AveragePrice { get; set; }
        public uint QuantityListed { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Represents a price alert when an item gets undercut
    /// </summary>
    public class PriceAlert
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public uint OldPrice { get; set; }
        public uint NewPrice { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool Acknowledged { get; set; } = false;
    }

    /// <summary>
    /// Trend analysis for an item's price history
    /// </summary>
    public class PriceTrend
    {
        public uint LowPrice { get; set; }
        public uint HighPrice { get; set; }
        public uint AveragePrice { get; set; }
        public int SampleCount { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string TrendDirection { get; set; } = "Stable";  // Rising, Falling, Stable
    }
}
