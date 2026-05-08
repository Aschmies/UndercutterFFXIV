using System;
using System.Collections.Generic;

namespace UndercutterFFXIV.Models
{
    /// <summary>
    /// Represents a potential market flipping opportunity
    /// </summary>
    public class FlipOpportunity
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public uint BuyPrice { get; set; }      // Min price in history
        public uint SellPrice { get; set; }     // Max price in history
        public uint CurrentPrice { get; set; }
        public uint AveragePrice { get; set; }
        public uint ProfitPerUnit => SellPrice > BuyPrice ? SellPrice - BuyPrice : 0;
        public double ProfitPercent => BuyPrice > 0 ? (ProfitPerUnit * 100.0) / BuyPrice : 0;
        public double Volatility { get; set; }  // (High - Low) / Average
        public string Trend { get; set; } = "Stable";  // Rising, Falling, Stable
        public int SamplesCount { get; set; }
        public ulong ProfitPotential => (ulong)ProfitPerUnit * 100;  // For sorting
    }

    /// <summary>
    /// Records a completed flip transaction
    /// </summary>
    public class FlipTransaction
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public uint BuyPrice { get; set; }
        public uint SellPrice { get; set; }
        public uint Quantity { get; set; }
        public DateTime BuyTime { get; set; } = DateTime.Now;
        public DateTime SellTime { get; set; } = DateTime.Now;
        public double TaxPercentage { get; set; } = 5.0;

        public ulong BuyCost => (ulong)BuyPrice * Quantity;
        public ulong SellRevenue => (ulong)SellPrice * Quantity;
        public ulong TaxAmount => (ulong)(SellRevenue * (TaxPercentage / 100.0));
        public ulong NetProfit => SellRevenue > TaxAmount ? SellRevenue - TaxAmount : 0;
        public double HoldingTime
        {
            get
            {
                var span = SellTime - BuyTime;
                return span.TotalHours;
            }
        }
    }

    /// <summary>
    /// Summary statistics for all flip transactions in a period
    /// </summary>
    public class FlipStatistics
    {
        public int TotalFlips { get; set; }
        public ulong TotalProfit { get; set; }
        public ulong TotalVolume { get; set; }
        public double AverageProfitPercentage { get; set; }
        public double AverageHoldingHours { get; set; }
        public uint MostFlippedItemId { get; set; }
        public string MostFlippedItemName { get; set; } = "";
        public ulong HighestSingleProfit { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }

    /// <summary>
    /// Item on the flip watchlist
    /// </summary>
    public class WatchlistItem
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public uint TargetBuyPrice { get; set; }
        public uint TargetSellPrice { get; set; }
        public string Notes { get; set; } = "";
        public bool Active { get; set; } = true;
        public DateTime AddedDate { get; set; } = DateTime.Now;
    }
}
