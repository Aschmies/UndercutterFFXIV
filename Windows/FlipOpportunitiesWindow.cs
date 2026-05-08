using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using UndercutterFFXIV.Services;

namespace UndercutterFFXIV.Windows
{
    /// <summary>
    /// Displays item flipping opportunities and recommendations
    /// </summary>
    public class FlipOpportunitiesWindow : Window, IDisposable
    {
        private FlipAnalyzerService flipAnalyzer { get; }
        private FlipTrackerService flipTracker { get; }
        private MarketTracker tracker { get; }

        private int filterTab = 0;
        private int minProfitFilter = 100;
        private float minProfitPercentFilter = 5.0f;

        public FlipOpportunitiesWindow(MarketTracker tracker, FlipAnalyzerService analyzer, FlipTrackerService flipTracker) : base(
            "Flip Opportunities###FlipOpportunitiesWindow",
            ImGuiWindowFlags.NoScrollbar)
        {
            this.tracker = tracker;
            this.flipAnalyzer = analyzer;
            this.flipTracker = flipTracker;

            Size = new Vector2(1000, 600);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            ImGui.TextWrapped("📊 Item Flipping Analysis - Find profitable buy/sell opportunities");
            ImGui.Separator();

            ImGui.SliderInt("Min Profit (gil)##flipMinProfit", ref minProfitFilter, 0, 10000);
            ImGui.SameLine();
            ImGui.SliderFloat("Min Profit %##flipMinPercent", ref minProfitPercentFilter, 0.0f, 50.0f, "%.1f%%");

            if (ImGui.Button("Analyze Now##flipAnalyze"))
            {
                var watchlistItems = flipTracker.GetWatchlist().Select(w => (w.ItemId, w.ItemName));
                flipAnalyzer.AnalyzeForFlips(minProfitFilter, (double)minProfitPercentFilter, watchlistItems);
            }

            ImGui.Separator();

            if (ImGui.BeginTabBar("##flipTabs"))
            {
                if (ImGui.BeginTabItem("All Opportunities##flipAll"))
                {
                    DrawAllOpportunities();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Buy Now##flipBuy"))
                {
                    DrawBuyingOpportunities();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Sell Now##flipSell"))
                {
                    DrawSellingOpportunities();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("High Volatility##flipVolatile"))
                {
                    DrawVolatileItems();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawAllOpportunities()
        {
            var opportunities = flipAnalyzer.GetAllOpportunities()
                .Where(o => o.ProfitPerUnit >= minProfitFilter && o.ProfitPercent >= minProfitPercentFilter)
                .OrderByDescending(o => o.ProfitPotential)
                .ToList();

            if (opportunities.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No opportunities found. Lower your profit thresholds or analyze more items.");
                return;
            }

            ImGui.Text($"Found {opportunities.Count} flip opportunities");
            ImGui.Separator();

            if (ImGui.BeginTable("##flipOpportunitiesTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item Name");
                ImGui.TableSetupColumn("Buy Price");
                ImGui.TableSetupColumn("Sell Price");
                ImGui.TableSetupColumn("Profit/Unit");
                ImGui.TableSetupColumn("Profit %");
                ImGui.TableSetupColumn("Volatility");
                ImGui.TableSetupColumn("Trend");
                ImGui.TableSetupColumn("Action");
                ImGui.TableHeadersRow();

                foreach (var opp in opportunities)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(opp.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.Text(opp.BuyPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), opp.SellPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), opp.ProfitPerUnit.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text($"{opp.ProfitPercent:F1}%");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{(opp.Volatility * 100):F1}%");

                    ImGui.TableNextColumn();
                    var trendColor = opp.Trend switch
                    {
                        "Rising" => new Vector4(1, 0.5f, 0, 1),
                        "Falling" => new Vector4(0, 1, 0, 1),
                        _ => new Vector4(1, 1, 1, 1)
                    };
                    ImGui.TextColored(trendColor, opp.Trend);

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Add to Watchlist##flip_{opp.ItemId}"))
                    {
                        flipTracker.AddToWatchlist(opp.ItemId, opp.ItemName, opp.BuyPrice, opp.SellPrice,
                            $"Profit potential: {opp.ProfitPercent:F1}%");
                        LoggingService.LogInfo($"Added {opp.ItemName} to flip watchlist");
                    }
                }

                ImGui.EndTable();
            }
        }

        private void DrawBuyingOpportunities()
        {
            var opportunities = flipAnalyzer.FindBuyingOpportunities();

            ImGui.TextWrapped("💰 Items currently at LOW prices - Good time to BUY for flipping");
            ImGui.Separator();

            if (opportunities.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No buying opportunities found at the moment.");
                return;
            }

            if (ImGui.BeginTable("##flipBuyTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Current Price");
                ImGui.TableSetupColumn("Average Price");
                ImGui.TableSetupColumn("Discount");
                ImGui.TableSetupColumn("Sell Potential");
                ImGui.TableSetupColumn("Profit/Unit");
                ImGui.TableSetupColumn("Action");
                ImGui.TableHeadersRow();

                foreach (var opp in opportunities)
                {
                    var discount = opp.AveragePrice > 0 ? ((opp.AveragePrice - opp.CurrentPrice) * 100.0 / opp.AveragePrice) : 0;
                    var profitIfBought = opp.SellPrice - opp.CurrentPrice;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(opp.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), opp.CurrentPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(opp.AveragePrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), $"{discount:F1}%");

                    ImGui.TableNextColumn();
                    ImGui.Text(opp.SellPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), profitIfBought.ToString("N0"));

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Watch##buy_{opp.ItemId}"))
                    {
                        flipTracker.AddToWatchlist(opp.ItemId, opp.ItemName, opp.CurrentPrice, opp.SellPrice,
                            $"Buy now at {opp.CurrentPrice}, sell at {opp.SellPrice}");
                    }
                }

                ImGui.EndTable();
            }
        }

        private void DrawSellingOpportunities()
        {
            var opportunities = flipAnalyzer.FindSellingOpportunities();

            ImGui.TextWrapped("📈 Items currently at HIGH prices - Good time to SELL your flipped items");
            ImGui.Separator();

            if (opportunities.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No selling opportunities found at the moment.");
                return;
            }

            if (ImGui.BeginTable("##flipSellTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Current Price");
                ImGui.TableSetupColumn("Average Price");
                ImGui.TableSetupColumn("Premium");
                ImGui.TableSetupColumn("Buy Price");
                ImGui.TableSetupColumn("Profit/Unit");
                ImGui.TableSetupColumn("Action");
                ImGui.TableHeadersRow();

                foreach (var opp in opportunities)
                {
                    var premium = opp.AveragePrice > 0 ? ((opp.CurrentPrice - opp.AveragePrice) * 100.0 / opp.AveragePrice) : 0;
                    var profitIfSold = opp.CurrentPrice - opp.BuyPrice;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(opp.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), opp.CurrentPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(opp.AveragePrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"{premium:F1}%");

                    ImGui.TableNextColumn();
                    ImGui.Text(opp.BuyPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), profitIfSold.ToString("N0"));

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Track##sell_{opp.ItemId}"))
                    {
                        flipTracker.AddToWatchlist(opp.ItemId, opp.ItemName, opp.BuyPrice, opp.CurrentPrice,
                            $"Sell window open! Price at {opp.CurrentPrice}");
                    }
                }

                ImGui.EndTable();
            }
        }

        private void DrawVolatileItems()
        {
            var opportunities = flipAnalyzer.GetAllOpportunities()
                .Where(o => o.Volatility >= 0.05)
                .OrderByDescending(o => o.Volatility)
                .ToList();

            ImGui.TextWrapped("🎢 High volatility items - Prices swing wildly (best flipping potential)");
            ImGui.Separator();

            if (opportunities.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No volatile items found.");
                return;
            }

            if (ImGui.BeginTable("##flipVolatileTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Buy");
                ImGui.TableSetupColumn("Sell");
                ImGui.TableSetupColumn("Range");
                ImGui.TableSetupColumn("Volatility %");
                ImGui.TableSetupColumn("Profit/Unit");
                ImGui.TableSetupColumn("Profit %");
                ImGui.TableSetupColumn("Action");
                ImGui.TableHeadersRow();

                foreach (var opp in opportunities)
                {
                    var range = opp.SellPrice - opp.BuyPrice;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(opp.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.Text(opp.BuyPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), opp.SellPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(range.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"{(opp.Volatility * 100):F1}%");

                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), opp.ProfitPerUnit.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text($"{opp.ProfitPercent:F1}%");

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Add##volatile_{opp.ItemId}"))
                    {
                        flipTracker.AddToWatchlist(opp.ItemId, opp.ItemName, opp.BuyPrice, opp.SellPrice,
                            $"Volatile - swing range: {range} gil");
                    }
                }

                ImGui.EndTable();
            }
        }

        public void Dispose() { }
    }
}
