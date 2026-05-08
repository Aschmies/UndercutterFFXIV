using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Fetches market data from Universalis crowdsourced FFXIV price database
    /// </summary>
    public class UniversalisApiService
    {
        private static readonly HttpClient client = new();
        private const string BASE_URL = "https://universalis.app/api/v2";
        private const int TIMEOUT_MS = 10000;

        public UniversalisApiService()
        {
            client.Timeout = TimeSpan.FromMilliseconds(TIMEOUT_MS);
        }

        /// <summary>
        /// Get detailed market data for an item (price, history, volume)
        /// </summary>
        public async Task<MarketPriceSnapshot> GetPriceData(
            string worldName,
            uint itemId,
            string itemName)
        {
            try
            {
                var url = $"{BASE_URL}/{worldName}/{itemId}";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                uint lowestPrice = 0;
                uint medianPrice = 0;
                uint averagePrice = 0;
                uint quantity = 0;

                // Parse listings to get current market info
                if (root.TryGetProperty("listings", out var listings))
                {
                    var count = listings.GetArrayLength();
                    if (count > 0)
                    {
                        // Lowest price is first listing
                        lowestPrice = (uint)listings[0]
                            .GetProperty("pricePerUnit").GetInt32();

                        // Calculate average, total quantity, and collect prices for true median
                        var totalPrice = 0ul;
                        var allPrices = new List<uint>(count);
                        foreach (var listing in listings.EnumerateArray())
                        {
                            var p = (uint)listing
                                .GetProperty("pricePerUnit").GetInt32();
                            totalPrice += p;
                            allPrices.Add(p);
                            quantity += (uint)listing
                                .GetProperty("quantity").GetInt32();
                        }
                        averagePrice = (uint)(totalPrice / (ulong)count);

                        // True median: middle value of sorted price list
                        allPrices.Sort();
                        medianPrice = allPrices[count / 2];
                    }
                }

                return new MarketPriceSnapshot
                {
                    ItemId = itemId,
                    ItemName = itemName,
                    LowestPrice = lowestPrice,
                    MedianPrice = medianPrice,
                    AveragePrice = averagePrice,
                    QuantityListed = quantity,
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Universalis parse error for item {itemId}: {ex.Message}");
                return null;
            }
        }
    }
}
