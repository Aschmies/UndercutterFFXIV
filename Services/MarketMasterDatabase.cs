using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    public sealed class MarketMasterDatabase
    {
        private readonly string connectionString;
        private readonly object dbLock = new();

        public MarketMasterDatabase(string pluginConfigDirectory)
        {
            var dbPath = Path.Combine(pluginConfigDirectory, "market_master_pro.db");
            connectionString = $"Data Source={dbPath}";
            InitializeSchema();
        }

        public IReadOnlyList<WatchedItem> GetWatchedItems()
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT item_id, item_name, added_utc
FROM watched_items
ORDER BY item_name";

                using var reader = cmd.ExecuteReader();
                var list = new List<WatchedItem>();
                while (reader.Read())
                {
                    list.Add(new WatchedItem
                    {
                        ItemId = (uint)reader.GetInt64(0),
                        Name = reader.GetString(1),
                        AddedUtc = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    });
                }

                return list;
            }
        }

        public void AddOrUpdateWatchedItem(uint itemId, string itemName)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO watched_items(item_id, item_name, added_utc)
VALUES($id, $name, $added)
ON CONFLICT(item_id)
DO UPDATE SET item_name = excluded.item_name";
                cmd.Parameters.AddWithValue("$id", itemId);
                cmd.Parameters.AddWithValue("$name", itemName);
                cmd.Parameters.AddWithValue("$added", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveWatchedItem(uint itemId)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM watched_items WHERE item_id = $id";
                cmd.Parameters.AddWithValue("$id", itemId);
                cmd.ExecuteNonQuery();
            }
        }

        public void SaveScanResults(
            string homeWorld,
            string dataCenter,
            IReadOnlyList<ArbitrageOpportunity> opportunities,
            long durationMs)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();

                long runId;
                using (var runCmd = connection.CreateCommand())
                {
                    runCmd.Transaction = transaction;
                    runCmd.CommandText = @"
INSERT INTO scan_runs(run_utc, duration_ms, item_count, home_world, data_center)
VALUES($utc, $duration, $count, $home, $dc);
SELECT last_insert_rowid();";
                    runCmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
                    runCmd.Parameters.AddWithValue("$duration", durationMs);
                    runCmd.Parameters.AddWithValue("$count", opportunities.Count);
                    runCmd.Parameters.AddWithValue("$home", homeWorld);
                    runCmd.Parameters.AddWithValue("$dc", dataCenter);
                    runId = (long)runCmd.ExecuteScalar()!;
                }

                foreach (var opportunity in opportunities)
                {
                    using var oppCmd = connection.CreateCommand();
                    oppCmd.Transaction = transaction;
                    oppCmd.CommandText = @"
INSERT INTO opportunities(
    run_id,
    item_id,
    item_name,
    home_min_price,
    dc_lowest_price,
    net_profit_per_unit,
    profit_percent,
    sale_velocity_per_day,
    potential_bot
)
VALUES($run, $id, $name, $homeMin, $dcLow, $net, $percent, $velocity, $bot)";

                    oppCmd.Parameters.AddWithValue("$run", runId);
                    oppCmd.Parameters.AddWithValue("$id", opportunity.ItemId);
                    oppCmd.Parameters.AddWithValue("$name", opportunity.ItemName);
                    oppCmd.Parameters.AddWithValue("$homeMin", opportunity.HomeWorldMinPrice);
                    oppCmd.Parameters.AddWithValue("$dcLow", opportunity.DataCenterLowestPrice);
                    oppCmd.Parameters.AddWithValue("$net", opportunity.NetProfitPerUnit);
                    oppCmd.Parameters.AddWithValue("$percent", opportunity.ProfitPercent);
                    oppCmd.Parameters.AddWithValue("$velocity", opportunity.SaleVelocityPerDay);
                    oppCmd.Parameters.AddWithValue("$bot", opportunity.PotentialBotSellerPattern ? 1 : 0);
                    oppCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public IReadOnlyList<(DateTime DateUtc, double TotalNetProfit)> GetDailyProfitSeries(int days)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT substr(sr.run_utc, 1, 10) as day,
       SUM(o.net_profit_per_unit) as net
FROM opportunities o
JOIN scan_runs sr ON sr.id = o.run_id
WHERE sr.run_utc >= $cutoff
GROUP BY day
ORDER BY day";
                cmd.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-Math.Max(1, days)).ToString("O"));

                using var reader = cmd.ExecuteReader();
                var points = new List<(DateTime, double)>();
                while (reader.Read())
                {
                    var day = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                    var net = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                    points.Add((day, net));
                }

                return points;
            }
        }

        public void AddTradeHistoryEntry(TradeHistoryEntry entry)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO trade_history(item_id, item_name, buy_price, sell_price, quantity, traded_utc)
VALUES($itemId, $itemName, $buyPrice, $sellPrice, $quantity, $tradedUtc)";

                cmd.Parameters.AddWithValue("$itemId", entry.ItemId);
                cmd.Parameters.AddWithValue("$itemName", entry.ItemName);
                cmd.Parameters.AddWithValue("$buyPrice", entry.BuyPrice);
                cmd.Parameters.AddWithValue("$sellPrice", entry.SellPrice);
                cmd.Parameters.AddWithValue("$quantity", entry.Quantity);
                cmd.Parameters.AddWithValue("$tradedUtc", entry.TradedUtc.ToString("O"));
                cmd.ExecuteNonQuery();
            }
        }

        public IReadOnlyList<TradeHistoryEntry> GetTradeHistory(int days)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT id, item_id, item_name, buy_price, sell_price, quantity, traded_utc
FROM trade_history
WHERE traded_utc >= $cutoff
ORDER BY traded_utc DESC";
                cmd.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-Math.Max(1, days)).ToString("O"));

                using var reader = cmd.ExecuteReader();
                var rows = new List<TradeHistoryEntry>();
                while (reader.Read())
                {
                    rows.Add(new TradeHistoryEntry
                    {
                        Id = reader.GetInt64(0),
                        ItemId = (uint)reader.GetInt64(1),
                        ItemName = reader.GetString(2),
                        BuyPrice = (uint)reader.GetInt64(3),
                        SellPrice = (uint)reader.GetInt64(4),
                        Quantity = (uint)reader.GetInt64(5),
                        TradedUtc = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    });
                }

                return rows;
            }
        }

        public bool UpsertPendingBuyCapture(PendingBuyCaptureEntry entry)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO pending_buy_captures(
    listing_id,
    item_id,
    item_name,
    quantity,
    unit_price,
    total_tax,
    container_index,
    is_hq,
    town_id,
    captured_utc
)
VALUES(
    $listingId,
    $itemId,
    $itemName,
    $quantity,
    $unitPrice,
    $totalTax,
    $containerIndex,
    $isHq,
    $townId,
    $capturedUtc
)
ON CONFLICT(listing_id) DO NOTHING;";

                cmd.Parameters.AddWithValue("$listingId", (long)entry.ListingId);
                cmd.Parameters.AddWithValue("$itemId", entry.ItemId);
                cmd.Parameters.AddWithValue("$itemName", entry.ItemName);
                cmd.Parameters.AddWithValue("$quantity", entry.Quantity);
                cmd.Parameters.AddWithValue("$unitPrice", entry.UnitPrice);
                cmd.Parameters.AddWithValue("$totalTax", entry.TotalTax);
                cmd.Parameters.AddWithValue("$containerIndex", entry.ContainerIndex);
                cmd.Parameters.AddWithValue("$isHq", entry.IsHq ? 1 : 0);
                cmd.Parameters.AddWithValue("$townId", entry.TownId);
                cmd.Parameters.AddWithValue("$capturedUtc", entry.CapturedUtc.ToString("O"));

                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public IReadOnlyList<PendingBuyCaptureEntry> GetPendingBuyCaptures(int limit = 100)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT listing_id, item_id, item_name, quantity, unit_price, total_tax, container_index, is_hq, town_id, captured_utc
FROM pending_buy_captures
ORDER BY captured_utc DESC
LIMIT $limit";
                cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

                using var reader = cmd.ExecuteReader();
                var rows = new List<PendingBuyCaptureEntry>();
                while (reader.Read())
                {
                    rows.Add(new PendingBuyCaptureEntry
                    {
                        ListingId = (ulong)reader.GetInt64(0),
                        ItemId = (uint)reader.GetInt64(1),
                        ItemName = reader.GetString(2),
                        Quantity = (uint)reader.GetInt64(3),
                        UnitPrice = (uint)reader.GetInt64(4),
                        TotalTax = (uint)reader.GetInt64(5),
                        ContainerIndex = (ushort)reader.GetInt64(6),
                        IsHq = !reader.IsDBNull(7) && reader.GetInt64(7) == 1,
                        TownId = (byte)reader.GetInt64(8),
                        CapturedUtc = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    });
                }

                return rows;
            }
        }

        public void RemovePendingBuyCapture(ulong listingId)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM pending_buy_captures WHERE listing_id = $listingId";
                cmd.Parameters.AddWithValue("$listingId", (long)listingId);
                cmd.ExecuteNonQuery();
            }
        }

        public void SaveRetainerListingSnapshots(IReadOnlyList<RetainerListingSnapshot> snapshots)
        {
            if (snapshots.Count == 0)
                return;

            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();

                foreach (var snapshot in snapshots)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
INSERT INTO retainer_listing_snapshots(
    slot_index,
    item_id,
    item_name,
    current_price,
    suggested_price,
    is_undercut,
    scanned_utc
)
VALUES($slot, $itemId, $itemName, $currentPrice, $suggestedPrice, $isUndercut, $scannedUtc)";
                    cmd.Parameters.AddWithValue("$slot", snapshot.SlotIndex);
                    cmd.Parameters.AddWithValue("$itemId", snapshot.ItemId);
                    cmd.Parameters.AddWithValue("$itemName", snapshot.ItemName);
                    cmd.Parameters.AddWithValue("$currentPrice", snapshot.CurrentPrice);
                    cmd.Parameters.AddWithValue("$suggestedPrice", snapshot.SuggestedPrice);
                    cmd.Parameters.AddWithValue("$isUndercut", snapshot.IsUndercut ? 1 : 0);
                    cmd.Parameters.AddWithValue("$scannedUtc", snapshot.ScannedUtc.ToString("O"));
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public IReadOnlyList<RetainerListingSnapshot> GetRetainerListingSnapshots(int days)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT id, slot_index, item_id, item_name, current_price, suggested_price, is_undercut, scanned_utc
FROM retainer_listing_snapshots
WHERE scanned_utc >= $cutoff
ORDER BY scanned_utc DESC";
                cmd.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-Math.Max(1, days)).ToString("O"));

                using var reader = cmd.ExecuteReader();
                var rows = new List<RetainerListingSnapshot>();
                while (reader.Read())
                {
                    rows.Add(new RetainerListingSnapshot
                    {
                        Id = reader.GetInt64(0),
                        SlotIndex = reader.GetInt32(1),
                        ItemId = (uint)reader.GetInt64(2),
                        ItemName = reader.GetString(3),
                        CurrentPrice = (uint)reader.GetInt64(4),
                        SuggestedPrice = (uint)reader.GetInt64(5),
                        IsUndercut = reader.GetInt64(6) == 1,
                        ScannedUtc = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    });
                }

                return rows;
            }
        }

        public void SaveRecommendationFeedback(uint itemId, string itemName, bool accepted, double projectedNetGil)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO recommendation_feedback(item_id, item_name, accepted, projected_net_gil, action_utc)
VALUES($itemId, $itemName, $accepted, $projected, $actionUtc)";
                cmd.Parameters.AddWithValue("$itemId", itemId);
                cmd.Parameters.AddWithValue("$itemName", itemName);
                cmd.Parameters.AddWithValue("$accepted", accepted ? 1 : 0);
                cmd.Parameters.AddWithValue("$projected", projectedNetGil);
                cmd.Parameters.AddWithValue("$actionUtc", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
        }

        public RecommendationFeedbackSummary GetRecommendationFeedbackSummary(int days)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT
    SUM(CASE WHEN accepted = 1 THEN 1 ELSE 0 END) AS accepted_count,
    SUM(CASE WHEN accepted = 0 THEN 1 ELSE 0 END) AS rejected_count
FROM recommendation_feedback
WHERE action_utc >= $cutoff";
                cmd.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-Math.Max(1, days)).ToString("O"));

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return new RecommendationFeedbackSummary();

                var accepted = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                var rejected = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var total = accepted + rejected;
                var rate = total == 0 ? 0 : (accepted * 100.0) / total;

                return new RecommendationFeedbackSummary
                {
                    AcceptedCount = accepted,
                    RejectedCount = rejected,
                    AcceptanceRatePercent = rate
                };
            }
        }

        public double? GetAverageBuyPrice(uint itemId, int days)
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT AVG(buy_price)
FROM trade_history
WHERE item_id = $itemId AND traded_utc >= $cutoff";
                cmd.Parameters.AddWithValue("$itemId", itemId);
                cmd.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-Math.Max(1, days)).ToString("O"));

                var value = cmd.ExecuteScalar();
                if (value == null || value is DBNull)
                    return null;

                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
        }

        private void InitializeSchema()
        {
            lock (dbLock)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS watched_items (
    item_id INTEGER PRIMARY KEY,
    item_name TEXT NOT NULL,
    added_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS scan_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_utc TEXT NOT NULL,
    duration_ms INTEGER NOT NULL,
    item_count INTEGER NOT NULL,
    home_world TEXT NOT NULL,
    data_center TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS opportunities (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    item_name TEXT NOT NULL,
    home_min_price INTEGER NOT NULL,
    dc_lowest_price INTEGER NOT NULL,
    net_profit_per_unit REAL NOT NULL,
    profit_percent REAL NOT NULL,
    sale_velocity_per_day REAL NOT NULL,
    potential_bot INTEGER NOT NULL,
    FOREIGN KEY(run_id) REFERENCES scan_runs(id)
);

CREATE TABLE IF NOT EXISTS trade_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    item_id INTEGER NOT NULL,
    item_name TEXT NOT NULL,
    buy_price INTEGER NOT NULL,
    sell_price INTEGER NOT NULL,
    quantity INTEGER NOT NULL,
    traded_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS pending_buy_captures (
    listing_id INTEGER PRIMARY KEY,
    item_id INTEGER NOT NULL,
    item_name TEXT NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price INTEGER NOT NULL,
    total_tax INTEGER NOT NULL,
    container_index INTEGER NOT NULL,
    is_hq INTEGER NOT NULL,
    town_id INTEGER NOT NULL,
    captured_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS retainer_listing_snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    slot_index INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    item_name TEXT NOT NULL,
    current_price INTEGER NOT NULL,
    suggested_price INTEGER NOT NULL,
    is_undercut INTEGER NOT NULL,
    scanned_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS recommendation_feedback (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    item_id INTEGER NOT NULL,
    item_name TEXT NOT NULL,
    accepted INTEGER NOT NULL,
    projected_net_gil REAL NOT NULL,
    action_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_opportunities_run ON opportunities(run_id);
CREATE INDEX IF NOT EXISTS idx_opportunities_item ON opportunities(item_id);
CREATE INDEX IF NOT EXISTS idx_trade_history_time ON trade_history(traded_utc);
CREATE INDEX IF NOT EXISTS idx_pending_buys_time ON pending_buy_captures(captured_utc);
CREATE INDEX IF NOT EXISTS idx_retainer_snapshots_time ON retainer_listing_snapshots(scanned_utc);
CREATE INDEX IF NOT EXISTS idx_retainer_snapshots_item ON retainer_listing_snapshots(item_id);
CREATE INDEX IF NOT EXISTS idx_feedback_time ON recommendation_feedback(action_utc);
CREATE INDEX IF NOT EXISTS idx_feedback_item ON recommendation_feedback(item_id);";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
