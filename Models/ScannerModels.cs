using System;
using System.Collections.Generic;
using System.Linq;

namespace UndercutterFFXIV.Models
{
    public sealed class ItemLookup
    {
        public uint ItemId { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool IsGear { get; init; }
        public int RequiredLevel { get; init; }
        public int ItemLevel { get; init; }
    }

    public sealed class WatchedItem
    {
        public uint ItemId { get; init; }
        public string Name { get; init; } = string.Empty;
        public DateTime AddedUtc { get; init; }
        public bool IsAutoTracked { get; init; }
    }

    public sealed class RetainerSaleListing
    {
        public int SlotIndex { get; init; }
        public uint ItemId { get; init; }
        public string Name { get; init; } = string.Empty;
        public uint CurrentPrice { get; init; }
        public int OwnedQuantity { get; init; }
    }

    public sealed class SaleRecord
    {
        public DateTime TimestampUtc { get; init; }
        public uint PricePerUnit { get; init; }
        public uint Quantity { get; init; }
    }

    public sealed class ListingRecord
    {
        public uint PricePerUnit { get; init; }
        public uint Quantity { get; init; }
        public string SellerName { get; init; } = string.Empty;
        public string WorldName { get; init; } = string.Empty;
    }

    public sealed class MarketSnapshot
    {
        public uint ItemId { get; init; }
        public string ScopeName { get; init; } = string.Empty;
        public uint LowestPrice { get; init; }
        public IReadOnlyList<SaleRecord> RecentSales { get; init; } = Array.Empty<SaleRecord>();
        public IReadOnlyList<ListingRecord> Listings { get; init; } = Array.Empty<ListingRecord>();

        public DateTime? MostRecentSaleUtc
            => RecentSales.Count == 0 ? null : RecentSales.Max(s => s.TimestampUtc);
    }

    public sealed class ArbitrageOpportunity
    {
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint HomeWorldMinPrice { get; init; }
        public uint DataCenterLowestPrice { get; init; }
        public string BuyFromWorld { get; init; } = string.Empty;
        public double NetProfitPerUnit { get; init; }
        public double ProfitPercent { get; init; }
        public double SaleVelocityPerDay { get; init; }
        public int SalesCount24h { get; init; }
        public int UnitsSold24h { get; init; }
        public uint HomeWorldCurrentQtyListing { get; init; }
        public bool PotentialBotSellerPattern { get; init; }
        public int SafeBuyQty { get; init; }
        public int OwnedQuantity { get; init; }
        public double ConfidenceScore { get; init; }
        public double DataFreshnessMinutes { get; init; }
        public bool IsLowTrust { get; init; }
        public string TrustReason { get; init; } = string.Empty;
        public string TravelPlanSummary { get; init; } = string.Empty;
        public bool TravelWorthIt { get; init; }
        public int RecommendedBuyQty { get; init; }
        public int MaxAffordableQtyByCapital { get; init; }
        public double ProjectedBatchNetGil { get; init; }
        public string RouteSummary { get; init; } = string.Empty;
        public string RiskRegime { get; init; } = string.Empty;
        public string ExplainabilitySummary { get; init; } = string.Empty;
        public double ScoreVelocity { get; init; }
        public double ScoreSpread { get; init; }
        public double ScoreDepth { get; init; }
        public double ScoreVolatility { get; init; }
        public double ScoreFreshness { get; init; }
        public double ScoreApiPenalty { get; init; }
        public bool NeedsManualReview { get; init; }
        public DateTime ScannedUtc { get; init; }

        /// <summary>
        /// Suggests how many units are safe to buy without raising suspicion.
        /// Hard cap of 3: spread risk across many different items rather than
        /// buying large stacks of a single item.
        /// </summary>
        public static int ComputeSafeBuyQty(double velocityPerDay, bool potentialBotPattern)
        {
            // Base quantity from velocity: slow-moving items hold risk longer
            int qty;
            if (velocityPerDay < 0.5)
                qty = 1;
            else if (velocityPerDay < 2.0)
                qty = 2;
            else
                qty = 3;

            // Bot-heavy listings mean the market is risky; be more conservative
            if (potentialBotPattern && qty > 1)
                qty--;

            return qty;
        }
    }

    public sealed class ApiHealthSnapshot
    {
        public double AverageLatencyMs { get; init; }
        public double ErrorRate { get; init; }
        public DateTime? LastSuccessUtc { get; init; }

        public string SafeZone
        {
            get
            {
                var staleSeconds = LastSuccessUtc.HasValue
                    ? (DateTime.UtcNow - LastSuccessUtc.Value).TotalSeconds
                    : double.MaxValue;

                if (ErrorRate <= 0.1 && AverageLatencyMs <= 700 && staleSeconds <= 120)
                    return "Green";
                if (ErrorRate <= 0.25 && AverageLatencyMs <= 1800 && staleSeconds <= 300)
                    return "Yellow";
                return "Red";
            }
        }
    }

    public sealed class ScanTimingSnapshot
    {
        public long HomeFetchMs { get; init; }
        public long HomeFilterMs { get; init; }
        public long DcFetchMs { get; init; }
        public long EvaluationMs { get; init; }
        public long TotalMs { get; init; }
        public int CandidateCount { get; init; }
    }

    public sealed class TradeHistoryEntry
    {
        public long Id { get; init; }
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint BuyPrice { get; init; }
        public uint SellPrice { get; init; }
        public uint Quantity { get; init; }
        public DateTime TradedUtc { get; init; }
    }

    public sealed class PendingBuyCaptureEntry
    {
        public ulong ListingId { get; init; }
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint Quantity { get; init; }
        public uint UnitPrice { get; init; }
        public uint TotalTax { get; init; }
        public ushort ContainerIndex { get; init; }
        public bool IsHq { get; init; }
        public byte TownId { get; init; }
        public DateTime CapturedUtc { get; init; }
    }

    public sealed class RetainerListingSnapshot
    {
        public long Id { get; init; }
        public int SlotIndex { get; init; }
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint CurrentPrice { get; init; }
        public uint SuggestedPrice { get; init; }
        public bool IsUndercut { get; init; }
        public DateTime ScannedUtc { get; init; }
    }

    public sealed class RetainerSnapshotAnalytics
    {
        public int TotalSnapshots { get; init; }
        public double UndercutFrequencyPercent { get; init; }
        public double AverageSitHours { get; init; }
        public IReadOnlyList<(string ItemName, int PriceChanges)> FastestChurnItems { get; init; } = Array.Empty<(string, int)>();
    }

    public sealed class AdvancedHistoryAnalytics
    {
        public int TotalTrades { get; init; }
        public double WinRatePercent { get; init; }
        public double AverageNetGilPerHour { get; init; }
        public double MedianEstimatedHoldHours { get; init; }
        public IReadOnlyList<(string Category, double NetGil)> BestCategories { get; init; } = Array.Empty<(string, double)>();
        public IReadOnlyList<(string ItemName, int LossCount, double TotalLoss)> RepeatedLossItems { get; init; } = Array.Empty<(string, int, double)>();
    }

    public sealed class WatchlistSuggestion
    {
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    public sealed class RecommendationFeedbackSummary
    {
        public int AcceptedCount { get; init; }
        public int RejectedCount { get; init; }
        public double AcceptanceRatePercent { get; init; }
    }
}
