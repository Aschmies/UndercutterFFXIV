using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using UndercutterFFXIV.Services;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Windows
{
    /// <summary>
    /// Tracks completed flip transactions and shows flip profitability analysis
    /// </summary>
    public class FlipTrackerWindow : Window, IDisposable
    {
        private FlipTrackerService flipTracker { get; }
        private FlipAnalyzerService flipAnalyzer { get; }
        private MarketTracker tracker { get; }
        private SellSuggestionService sellSuggestions { get; }

        private int daysPeriod = 30;

        public FlipTrackerWindow(
            FlipTrackerService flipTracker,
            FlipAnalyzerService analyzer,
            MarketTracker tracker,
            SellSuggestionService sellSuggestions) : base(
            "Flip Tracker###FlipTrackerWindow",
            ImGuiWindowFlags.NoScrollbar)
        {
            this.flipTracker = flipTracker;
            this.flipAnalyzer = analyzer;
            this.tracker = tracker;
            this.sellSuggestions = sellSuggestions;

            Size = new Vector2(1000, 600);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            ImGui.TextWrapped("📊 Flip Tracking - Monitor your flipping profitability");
            ImGui.Separator();

            ImGui.SliderInt("Period (days)##flipPeriodDays", ref daysPeriod, 1, 90);

            if (ImGui.BeginTabBar("##flipTrackerTabs"))
            {
                if (ImGui.BeginTabItem("Statistics"))
                {
                    DrawStatistics();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Recent Flips"))
                {
                    DrawRecentFlips();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Per-Item Analysis"))
                {
                    DrawPerItemStats();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Watchlist"))
                {
                    DrawWatchlist();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Active Flips"))
                {
                    DrawActiveFlips();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawActiveFlips()
        {
            sellSuggestions.UpdateMarketPrices();

            var activeFlips = sellSuggestions.GetActiveFlips();
            if (activeFlips.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No active flips tracked yet.");
                ImGui.TextWrapped("Use the Active Flips window (/ma active) to add and monitor purchased items.");
                return;
            }

            var summary = sellSuggestions.GetActiveSummary();
            ImGui.Text($"Active: {summary.count} | Ready: {summary.readyToSell} | Potential Profit: {summary.totalPotentialProfit:N0} gil");
            ImGui.Separator();

            if (ImGui.BeginTable("##activeFlipsTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Buy");
                ImGui.TableSetupColumn("Current");
                ImGui.TableSetupColumn("Target");
                ImGui.TableSetupColumn("Profit");
                ImGui.TableSetupColumn("Progress");
                ImGui.TableSetupColumn("Signal");
                ImGui.TableSetupColumn("Action");
                ImGui.TableHeadersRow();

                foreach (var flip in activeFlips.OrderByDescending(f => f.IsReadyToSell).ThenByDescending(f => f.PotentialProfit))
                {
                    var progress = flip.TargetSellPrice > 0
                        ? Math.Min(100, (flip.CurrentLowestPrice * 100.0) / flip.TargetSellPrice)
                        : 0;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(flip.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.Text(flip.BuyPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), flip.CurrentLowestPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(flip.TargetSellPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    var profitColor = flip.PotentialProfit > 0 ? new Vector4(0.2f, 1f, 0.2f, 1f) : new Vector4(1f, 0.4f, 0.4f, 1f);
                    ImGui.TextColored(profitColor, $"{flip.PotentialProfit:N0}");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{progress:F1}%");

                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(flip.Reason);

                    ImGui.TableNextColumn();
                    if (flip.IsReadyToSell && ImGui.SmallButton($"Mark Sold##sell_{flip.ItemId}"))
                    {
                        flipTracker.RecordFlip(
                            flip.ItemId,
                            flip.ItemName,
                            flip.BuyPrice,
                            flip.CurrentLowestPrice,
                            flip.Quantity,
                            5.0);
                        sellSuggestions.CompleteFlip(flip.ItemId);
                    }

                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Cancel##cancel_{flip.ItemId}"))
                    {
                        sellSuggestions.CancelFlip(flip.ItemId);
                    }
                }

                ImGui.EndTable();
            }
        }

        private void DrawStatistics()
        {
            var stats = flipTracker.GetStatistics(daysPeriod);

            if (stats.TotalFlips == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No flip transactions recorded yet. Complete your first flip to see statistics.");
                return;
            }

            ImGui.Text("=== Flip Summary ===");
            ImGui.Text($"Period: Last {daysPeriod} days");
            ImGui.Text($"Total Flips: {stats.TotalFlips}");
            ImGui.Text($"Total Profit: {stats.TotalProfit:N0} gil");
            ImGui.Text($"Total Volume: {stats.TotalVolume:N0} gil");
            ImGui.Text($"Average Profit %: {stats.AverageProfitPercentage:F2}%");
            ImGui.Text($"Average Holding Time: {stats.AverageHoldingHours:F1} hours");

            ImGui.Separator();
            ImGui.Text("=== Top Performer ===");
            ImGui.Text($"Item: {stats.MostFlippedItemName}");
            ImGui.Text($"Highest Single Flip Profit: {stats.HighestSingleProfit:N0} gil");

            ImGui.Separator();
            if (stats.TotalFlips > 0)
            {
                var dailyAverage = stats.TotalProfit / (ulong)daysPeriod;
                var profitPerFlip = stats.TotalProfit / (ulong)stats.TotalFlips;
                ImGui.Text("=== Averages ===");
                ImGui.Text($"Profit per flip: {profitPerFlip:N0} gil");
                ImGui.Text($"Daily profit average: {dailyAverage:N0} gil/day");
            }
        }

        private void DrawRecentFlips()
        {
            var transactions = flipTracker.GetTransactions(daysPeriod);

            if (transactions.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No flip transactions recorded yet.");
                return;
            }

            ImGui.Text($"Recent Transactions: {transactions.Count}");
            ImGui.Separator();

            if (ImGui.BeginTable("##flipTransactionsTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Buy Price");
                ImGui.TableSetupColumn("Sell Price");
                ImGui.TableSetupColumn("Quantity");
                ImGui.TableSetupColumn("Profit After Tax");
                ImGui.TableSetupColumn("Margin %");
                ImGui.TableSetupColumn("Hold Time");
                ImGui.TableHeadersRow();

                var totalProfit = 0ul;
                foreach (var flip in transactions)
                {
                    totalProfit += flip.NetProfit;

                    var margin = flip.SellPrice > flip.BuyPrice ? ((flip.SellPrice - flip.BuyPrice) * 100.0 / flip.BuyPrice) : 0;
                    var holdText = flip.HoldingTime < 1 ? $"{flip.HoldingTime * 60:F0}m" : $"{flip.HoldingTime:F1}h";

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(flip.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.Text(flip.BuyPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), flip.SellPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(flip.Quantity.ToString());

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), flip.NetProfit.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text($"{margin:F1}%");

                    ImGui.TableNextColumn();
                    ImGui.Text(holdText);
                }

                ImGui.EndTable();

                ImGui.Separator();
                ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Total Profit (shown): {totalProfit:N0} gil");
            }
        }

        private void DrawPerItemStats()
        {
            var perItemStats = flipTracker.GetPerItemStats(daysPeriod);

            if (perItemStats.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No flip data available yet.");
                return;
            }

            ImGui.Text($"Items Flipped: {perItemStats.Count}");
            ImGui.Separator();

            if (ImGui.BeginTable("##flipPerItemTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item ID");
                ImGui.TableSetupColumn("Flips");
                ImGui.TableSetupColumn("Total Profit");
                ImGui.TableSetupColumn("Avg Margin %");
                ImGui.TableSetupColumn("ROI");
                ImGui.TableHeadersRow();

                var sortedItems = perItemStats.OrderByDescending(x => x.Value.profit).ToList();

                foreach (var item in sortedItems)
                {
                    var (count, profit, margin) = item.Value;
                    var roi = margin;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(item.Key.ToString());

                    ImGui.TableNextColumn();
                    ImGui.Text(count.ToString());

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), profit.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text($"{margin:F1}%");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{roi:F1}%");
                }

                ImGui.EndTable();
            }
        }

        private void DrawWatchlist()
        {
            var watchlist = flipTracker.GetWatchlist();
            var alerts = flipTracker.GetWatchlistAlerts(tracker);

            ImGui.Text($"Watchlist Items: {watchlist.Count}");
            if (alerts.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), $"⚠️ {alerts.Count} Alerts");
            }
            ImGui.Separator();

            if (watchlist.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Your watchlist is empty. Add items from Flip Opportunities tab.");
                return;
            }

            if (ImGui.BeginTable("##flipWatchlistTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Target Buy");
                ImGui.TableSetupColumn("Target Sell");
                ImGui.TableSetupColumn("Profit Goal");
                ImGui.TableSetupColumn("Status");
                ImGui.TableSetupColumn("Action");
                ImGui.TableHeadersRow();

                foreach (var item in watchlist)
                {
                    var isAlert = alerts.Any(a => a.ItemId == item.ItemId);
                    var statusColor = isAlert ? new Vector4(1, 1, 0, 1) : new Vector4(0.5f, 0.5f, 0.5f, 1);
                    var statusText = isAlert ? "🔔 READY" : "Waiting";

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(item.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.Text(item.TargetBuyPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(item.TargetSellPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    var profitGoal = item.TargetSellPrice > item.TargetBuyPrice
                        ? item.TargetSellPrice - item.TargetBuyPrice : 0;
                    ImGui.Text(profitGoal.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(statusColor, statusText);

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Remove##watch_{item.ItemId}"))
                    {
                        flipTracker.RemoveFromWatchlist(item.ItemId);
                    }
                }

                ImGui.EndTable();
            }
        }

        public void Dispose() { }
    }
}
