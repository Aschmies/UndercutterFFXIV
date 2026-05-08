using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Handles JSON persistence for market data
    /// </summary>
    public class PersistenceService
    {
        private string DataDirectory { get; }
        private JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public PersistenceService(string pluginConfigDirectory)
        {
            DataDirectory = Path.Combine(pluginConfigDirectory, "data");
            Directory.CreateDirectory(DataDirectory);
        }

        public void SaveTrackedItems(List<ListedItem> items)
        {
            try
            {
                var path = Path.Combine(DataDirectory, "tracked_items.json");
                var json = JsonSerializer.Serialize(items, JsonOptions);
                File.WriteAllText(path, json);
                LoggingService.LogInfo($"Saved {items.Count} tracked items");
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to save tracked items: {ex.Message}");
            }
        }

        public List<ListedItem> LoadTrackedItems()
        {
            try
            {
                var path = Path.Combine(DataDirectory, "tracked_items.json");
                if (!File.Exists(path))
                    return new List<ListedItem>();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<ListedItem>>(json) ?? new List<ListedItem>();
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to load tracked items: {ex.Message}");
                return new List<ListedItem>();
            }
        }

        public void SavePriceHistory(Dictionary<uint, List<MarketPriceSnapshot>> history)
        {
            try
            {
                var path = Path.Combine(DataDirectory, "price_history.json");
                var json = JsonSerializer.Serialize(history, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to save price history: {ex.Message}");
            }
        }

        public Dictionary<uint, List<MarketPriceSnapshot>> LoadPriceHistory()
        {
            try
            {
                var path = Path.Combine(DataDirectory, "price_history.json");
                if (!File.Exists(path))
                    return new Dictionary<uint, List<MarketPriceSnapshot>>();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<uint, List<MarketPriceSnapshot>>>(json) 
                    ?? new Dictionary<uint, List<MarketPriceSnapshot>>();
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to load price history: {ex.Message}");
                return new Dictionary<uint, List<MarketPriceSnapshot>>();
            }
        }

        public void SaveAlerts(List<PriceAlert> alerts)
        {
            try
            {
                var path = Path.Combine(DataDirectory, "alerts.json");
                var json = JsonSerializer.Serialize(alerts, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to save alerts: {ex.Message}");
            }
        }

        public List<PriceAlert> LoadAlerts()
        {
            try
            {
                var path = Path.Combine(DataDirectory, "alerts.json");
                if (!File.Exists(path))
                    return new List<PriceAlert>();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<PriceAlert>>(json) ?? new List<PriceAlert>();
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to load alerts: {ex.Message}");
                return new List<PriceAlert>();
            }
        }

        public void SaveFlipTransactions(List<FlipTransaction> transactions)
        {
            try
            {
                var path = Path.Combine(DataDirectory, "flip_transactions.json");
                var json = JsonSerializer.Serialize(transactions, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to save flip transactions: {ex.Message}");
            }
        }

        public List<FlipTransaction> LoadFlipTransactions()
        {
            try
            {
                var path = Path.Combine(DataDirectory, "flip_transactions.json");
                if (!File.Exists(path))
                    return new List<FlipTransaction>();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<FlipTransaction>>(json) ?? new List<FlipTransaction>();
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to load flip transactions: {ex.Message}");
                return new List<FlipTransaction>();
            }
        }

        public void SaveWatchlist(List<WatchlistItem> items)
        {
            try
            {
                var path = Path.Combine(DataDirectory, "watchlist.json");
                var json = JsonSerializer.Serialize(items, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to save watchlist: {ex.Message}");
            }
        }

        public List<WatchlistItem> LoadWatchlist()
        {
            try
            {
                var path = Path.Combine(DataDirectory, "watchlist.json");
                if (!File.Exists(path))
                    return new List<WatchlistItem>();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<WatchlistItem>>(json) ?? new List<WatchlistItem>();
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to load watchlist: {ex.Message}");
                return new List<WatchlistItem>();
            }
        }

        public void ClearOldData(int daysToKeep = 90)
        {
            // TODO: Implement old data cleanup
        }
    }
}
