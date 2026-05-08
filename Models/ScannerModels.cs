using System;
using System.Collections.Generic;

namespace UndercutterFFXIV.Models
{
    public sealed class ItemLookup
    {
        public uint ItemId { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    public sealed class WatchedItem
    {
        public uint ItemId { get; init; }
        public string Name { get; init; } = string.Empty;
        public DateTime AddedUtc { get; init; }
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
        public string SellerName { get; init; } = string.Empty;
    }

    public sealed class MarketSnapshot
    {
        public uint ItemId { get; init; }
        public string ScopeName { get; init; } = string.Empty;
        public uint LowestPrice { get; init; }
        public IReadOnlyList<SaleRecord> RecentSales { get; init; } = Array.Empty<SaleRecord>();
        public IReadOnlyList<ListingRecord> Listings { get; init; } = Array.Empty<ListingRecord>();
    }

    public sealed class ArbitrageOpportunity
    {
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint HomeWorldMinPrice { get; init; }
        public uint DataCenterLowestPrice { get; init; }
        public double NetProfitPerUnit { get; init; }
        public double ProfitPercent { get; init; }
        public double SaleVelocityPerDay { get; init; }
        public bool PotentialBotSellerPattern { get; init; }
        public DateTime ScannedUtc { get; init; }
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
}
