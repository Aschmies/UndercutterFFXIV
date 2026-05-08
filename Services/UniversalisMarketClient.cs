using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    public sealed class UniversalisMarketClient
    {
        private readonly HttpClient http;
        private readonly object metricsLock = new();
        private readonly Queue<(bool Ok, long LatencyMs)> recentMetrics = new();
        private DateTime? lastSuccessUtc;

        public UniversalisMarketClient(HttpClient http)
        {
            this.http = http;
            this.http.Timeout = TimeSpan.FromSeconds(15);
            this.http.DefaultRequestHeaders.UserAgent.ParseAdd("MarketMasterPro/1.1");
        }

        public async Task<MarketSnapshot?> GetMarketSnapshotAsync(string scopeName, uint itemId, CancellationToken cancellationToken)
        {
            var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(scopeName)}/{itemId}?listings=40&entries=180";
            var sw = Stopwatch.StartNew();

            try
            {
                using var response = await http.GetAsync(url, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    AddMetric(false, sw.ElapsedMilliseconds);
                    return null;
                }

                using var json = JsonDocument.Parse(content);
                var root = json.RootElement;

                var listings = new List<ListingRecord>();
                if (root.TryGetProperty("listings", out var listingsNode) && listingsNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var listing in listingsNode.EnumerateArray())
                    {
                        var price = TryGetUInt(listing, "pricePerUnit");
                        var seller = TryGetString(listing, "retainerName");
                        if (price > 0)
                        {
                            listings.Add(new ListingRecord
                            {
                                PricePerUnit = price,
                                SellerName = seller
                            });
                        }
                    }
                }

                var recentSales = new List<SaleRecord>();
                if (root.TryGetProperty("recentHistory", out var historyNode) && historyNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in historyNode.EnumerateArray())
                    {
                        var price = TryGetUInt(entry, "pricePerUnit");
                        var quantity = TryGetUInt(entry, "quantity");
                        var timestamp = TryGetLong(entry, "timestamp");
                        if (price == 0 || timestamp <= 0)
                            continue;

                        recentSales.Add(new SaleRecord
                        {
                            PricePerUnit = price,
                            Quantity = quantity == 0 ? 1u : quantity,
                            TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime
                        });
                    }
                }

                var lowest = listings.Count == 0 ? 0u : listings.Min(l => l.PricePerUnit);

                AddMetric(true, sw.ElapsedMilliseconds);
                return new MarketSnapshot
                {
                    ItemId = itemId,
                    ScopeName = scopeName,
                    LowestPrice = lowest,
                    Listings = listings,
                    RecentSales = recentSales
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                AddMetric(false, sw.ElapsedMilliseconds);
                LoggingService.LogWarning($"Universalis request failed for {scopeName}/{itemId}: {ex.Message}");
                return null;
            }
        }

        public ApiHealthSnapshot GetHealthSnapshot()
        {
            lock (metricsLock)
            {
                if (recentMetrics.Count == 0)
                {
                    return new ApiHealthSnapshot
                    {
                        AverageLatencyMs = 0,
                        ErrorRate = 0,
                        LastSuccessUtc = lastSuccessUtc
                    };
                }

                var arr = recentMetrics.ToArray();
                var avg = arr.Average(m => m.LatencyMs);
                var err = arr.Count(m => !m.Ok) / (double)arr.Length;

                return new ApiHealthSnapshot
                {
                    AverageLatencyMs = avg,
                    ErrorRate = err,
                    LastSuccessUtc = lastSuccessUtc
                };
            }
        }

        private void AddMetric(bool ok, long latencyMs)
        {
            lock (metricsLock)
            {
                recentMetrics.Enqueue((ok, latencyMs));
                while (recentMetrics.Count > 200)
                    recentMetrics.Dequeue();

                if (ok)
                    lastSuccessUtc = DateTime.UtcNow;
            }
        }

        private static uint TryGetUInt(JsonElement parent, string property)
        {
            if (!parent.TryGetProperty(property, out var node))
                return 0;
            return node.ValueKind switch
            {
                JsonValueKind.Number when node.TryGetUInt32(out var n) => n,
                JsonValueKind.Number when node.TryGetInt64(out var i) && i > 0 => (uint)i,
                _ => 0
            };
        }

        private static long TryGetLong(JsonElement parent, string property)
        {
            if (!parent.TryGetProperty(property, out var node))
                return 0;
            return node.ValueKind switch
            {
                JsonValueKind.Number when node.TryGetInt64(out var n) => n,
                _ => 0
            };
        }

        private static string TryGetString(JsonElement parent, string property)
        {
            if (!parent.TryGetProperty(property, out var node))
                return string.Empty;
            return node.ValueKind == JsonValueKind.String ? (node.GetString() ?? string.Empty) : string.Empty;
        }
    }
}
