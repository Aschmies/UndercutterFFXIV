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
        private const int RequestTimeoutSeconds = 40;
        private const int MaxRetryAttempts = 2;
        private const int DefaultListings = 20;
        private const int DefaultEntries = 60;
        private const int BatchSize = 100;
        private readonly HttpClient http;
        private readonly object metricsLock = new();
        private readonly Queue<(bool Ok, long LatencyMs)> recentMetrics = new();
        private DateTime? lastSuccessUtc;

        public UniversalisMarketClient(HttpClient http)
        {
            this.http = http;
            this.http.Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);
            this.http.DefaultRequestHeaders.UserAgent.ParseAdd("MarketMasterPro/1.1");
        }

        public async Task<MarketSnapshot?> GetMarketSnapshotAsync(string scopeName, uint itemId, CancellationToken cancellationToken)
        {
            var snapshots = await GetMarketSnapshotsAsync(scopeName, new[] { itemId }, cancellationToken);
            return snapshots.TryGetValue(itemId, out var snapshot) ? snapshot : null;
        }

        public async Task<Dictionary<uint, MarketSnapshot>> GetMarketSnapshotsAsync(
            string scopeName,
            IReadOnlyList<uint> itemIds,
            CancellationToken cancellationToken)
        {
            var normalizedScope = (scopeName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedScope))
            {
                AddMetric(false, 0);
                LoggingService.LogWarning("Universalis batch request skipped: empty scope name");
                return new Dictionary<uint, MarketSnapshot>();
            }

            if (itemIds.Count == 0)
                return new Dictionary<uint, MarketSnapshot>();

            var uniqueItemIds = itemIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var allSnapshots = new Dictionary<uint, MarketSnapshot>(uniqueItemIds.Count);
            for (var index = 0; index < uniqueItemIds.Count; index += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = uniqueItemIds
                    .Skip(index)
                    .Take(BatchSize)
                    .ToList();
                var chunkSnapshots = await GetBatchSnapshotsAsync(normalizedScope, chunk, cancellationToken);

                foreach (var kvp in chunkSnapshots)
                    allSnapshots[kvp.Key] = kvp.Value;
            }

            return allSnapshots;
        }

        private async Task<Dictionary<uint, MarketSnapshot>> GetBatchSnapshotsAsync(
            string normalizedScope,
            IReadOnlyList<uint> itemIds,
            CancellationToken cancellationToken)
        {
            var encodedItemIds = string.Join(",", itemIds.Select(id => id.ToString()));
            var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(normalizedScope)}/{encodedItemIds}?listings={DefaultListings}&entries={DefaultEntries}";
            var sw = Stopwatch.StartNew();
            Exception? lastException = null;

            for (var attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    using var response = await http.GetAsync(url, cancellationToken);
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        if (ShouldRetry(response.StatusCode) && attempt < MaxRetryAttempts)
                        {
                            await Task.Delay(GetRetryDelayMs(attempt), cancellationToken);
                            continue;
                        }

                        sw.Stop();
                        AddMetric(false, sw.ElapsedMilliseconds);
                        LoggingService.LogWarning($"Universalis batch non-success for {normalizedScope}: {(int)response.StatusCode} {response.ReasonPhrase}");
                        return new Dictionary<uint, MarketSnapshot>();
                    }

                    using var json = JsonDocument.Parse(content);
                    var snapshots = ParseSnapshots(json.RootElement, normalizedScope, itemIds);

                    sw.Stop();
                    AddMetric(true, sw.ElapsedMilliseconds);
                    return snapshots;
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    lastException = ex;
                    if (attempt < MaxRetryAttempts)
                    {
                        await Task.Delay(GetRetryDelayMs(attempt), cancellationToken);
                        continue;
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    if (attempt < MaxRetryAttempts)
                    {
                        await Task.Delay(GetRetryDelayMs(attempt), cancellationToken);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    AddMetric(false, sw.ElapsedMilliseconds);
                    LoggingService.LogWarning($"Universalis batch request failed for {normalizedScope}: {ex.Message}");
                    return new Dictionary<uint, MarketSnapshot>();
                }
            }

            sw.Stop();
            AddMetric(false, sw.ElapsedMilliseconds);
            LoggingService.LogWarning($"Universalis batch request failed for {normalizedScope} after {MaxRetryAttempts + 1} attempts: {lastException?.Message ?? "Unknown failure"}");
            return new Dictionary<uint, MarketSnapshot>();
        }

        private static Dictionary<uint, MarketSnapshot> ParseSnapshots(
            JsonElement root,
            string scopeName,
            IReadOnlyList<uint> requestedItemIds)
        {
            var snapshots = new Dictionary<uint, MarketSnapshot>(requestedItemIds.Count);

            if (root.TryGetProperty("items", out var itemsNode) && itemsNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var itemId in requestedItemIds)
                {
                    if (!itemsNode.TryGetProperty(itemId.ToString(), out var itemNode))
                        continue;

                    snapshots[itemId] = ParseSnapshot(itemNode, scopeName, itemId);
                }

                return snapshots;
            }

            if (requestedItemIds.Count == 1)
            {
                var itemId = requestedItemIds[0];
                snapshots[itemId] = ParseSnapshot(root, scopeName, itemId);
            }

            return snapshots;
        }

        private static MarketSnapshot ParseSnapshot(JsonElement source, string scopeName, uint itemId)
        {
            var listings = new List<ListingRecord>();
            if (source.TryGetProperty("listings", out var listingsNode) && listingsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var listing in listingsNode.EnumerateArray())
                {
                    var price = TryGetUInt(listing, "pricePerUnit");
                    var quantity = TryGetUInt(listing, "quantity");
                    var seller = TryGetString(listing, "retainerName");
                    var world = TryGetString(listing, "worldName");
                    if (price > 0)
                    {
                        listings.Add(new ListingRecord
                        {
                            PricePerUnit = price,
                            Quantity = quantity == 0 ? 1u : quantity,
                            SellerName = seller,
                            WorldName = world
                        });
                    }
                }
            }

            var recentSales = new List<SaleRecord>();
            if (source.TryGetProperty("recentHistory", out var historyNode) && historyNode.ValueKind == JsonValueKind.Array)
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
            return new MarketSnapshot
            {
                ItemId = itemId,
                ScopeName = scopeName,
                LowestPrice = lowest,
                Listings = listings,
                RecentSales = recentSales
            };
        }

        private static bool ShouldRetry(System.Net.HttpStatusCode statusCode)
        {
            var numeric = (int)statusCode;
            return statusCode == System.Net.HttpStatusCode.RequestTimeout
                || statusCode == System.Net.HttpStatusCode.TooManyRequests
                || numeric >= 500;
        }

        private static int GetRetryDelayMs(int attempt)
        {
            // Small backoff keeps scan throughput reasonable while recovering from transient API slowdowns.
            return 300 + (attempt * 450);
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
