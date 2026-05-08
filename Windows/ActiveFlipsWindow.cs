using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Linq;
using System.Numerics;
using UndercutterFFXIV.Services;

namespace UndercutterFFXIV.Windows
{
    /// <summary>
    /// Dedicated view for active flips and sell signals.
    /// </summary>
    public class ActiveFlipsWindow : Window, IDisposable
    {
        private SellSuggestionService sellSuggestions { get; }
        private FlipTrackerService flipTracker { get; }

        private string itemName = "";
        private int itemId;
        private int buyPrice;
        private int quantity = 1;
        private int targetSellPrice;
        private int minProfitPerUnit;

        public ActiveFlipsWindow(SellSuggestionService sellSuggestions, FlipTrackerService flipTracker) : base(
            "Active Flips###ActiveFlipsWindow",
            ImGuiWindowFlags.None)
        {
            this.sellSuggestions = sellSuggestions;
            this.flipTracker = flipTracker;
            Size = new Vector2(980, 620);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            sellSuggestions.UpdateMarketPrices();

            DrawAddFlipPanel();
            ImGui.Separator();
            DrawActiveFlipsTable();
        }

        private void DrawAddFlipPanel()
        {
            ImGui.TextWrapped("Track a purchased item to receive sell timing suggestions.");

            ImGui.PushItemWidth(220);
            ImGui.InputText("Item Name##activeName", ref itemName, 128);
            ImGui.SameLine();
            ImGui.InputInt("Item ID##activeId", ref itemId);
            ImGui.SameLine();
            ImGui.InputInt("Qty##activeQty", ref quantity);

            ImGui.InputInt("Buy Price##activeBuy", ref buyPrice);
            ImGui.SameLine();
            ImGui.InputInt("Target Sell##activeTarget", ref targetSellPrice);
            ImGui.SameLine();
            ImGui.InputInt("Min Profit/Unit##activeMin", ref minProfitPerUnit);
            ImGui.PopItemWidth();

            if (ImGui.Button("Start Tracking Flip") && itemId > 0 && buyPrice > 0 && quantity > 0)
            {
                var finalName = string.IsNullOrWhiteSpace(itemName) ? $"Item {itemId}" : itemName.Trim();
                var finalTarget = targetSellPrice > 0 ? (uint)targetSellPrice : (uint)buyPrice + Math.Max(1u, (uint)minProfitPerUnit);
                sellSuggestions.StartFlip((uint)itemId, finalName, (uint)buyPrice, (uint)quantity, finalTarget, (uint)minProfitPerUnit);

                itemName = "";
                itemId = 0;
                buyPrice = 0;
                quantity = 1;
                targetSellPrice = 0;
                minProfitPerUnit = 0;
            }

            ImGui.SameLine();
            if (ImGui.Button("Refresh Signals"))
            {
                sellSuggestions.UpdateMarketPrices();
            }

            var summary = sellSuggestions.GetActiveSummary();
            ImGui.Text($"Active: {summary.count} | Ready: {summary.readyToSell} | Potential Profit: {summary.totalPotentialProfit:N0} gil");
        }

        private void DrawActiveFlipsTable()
        {
            var activeFlips = sellSuggestions.GetActiveFlips();
            if (activeFlips.Count == 0)
            {
                ImGui.TextColored(new Vector4(1f, 1f, 0.3f, 1f), "No active flips currently tracked.");
                return;
            }

            if (ImGui.BeginTable("##activeFlipsMonitorTable", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Buy");
                ImGui.TableSetupColumn("Current");
                ImGui.TableSetupColumn("Target");
                ImGui.TableSetupColumn("Qty");
                ImGui.TableSetupColumn("Profit");
                ImGui.TableSetupColumn("Hold Time");
                ImGui.TableSetupColumn("Suggestion");
                ImGui.TableSetupColumn("Action");
                ImGui.TableHeadersRow();

                foreach (var flip in activeFlips.OrderByDescending(f => f.IsReadyToSell).ThenByDescending(f => f.PotentialProfit))
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.Text(flip.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.Text(flip.BuyPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    var currentColor = flip.CurrentLowestPrice >= flip.BuyPrice
                        ? new Vector4(0.35f, 1f, 0.35f, 1f)
                        : new Vector4(1f, 0.4f, 0.4f, 1f);
                    ImGui.TextColored(currentColor, flip.CurrentLowestPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(flip.TargetSellPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(flip.Quantity.ToString("N0"));

                    ImGui.TableNextColumn();
                    var profitColor = flip.PotentialProfit > 0
                        ? new Vector4(0.2f, 1f, 0.2f, 1f)
                        : new Vector4(1f, 0.4f, 0.4f, 1f);
                    ImGui.TextColored(profitColor, $"{flip.PotentialProfit:N0} ({flip.ProfitPercent:F1}%)");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{flip.HoldingHours:F1}h");

                    ImGui.TableNextColumn();
                    var signalColor = flip.IsReadyToSell ? new Vector4(1f, 0.9f, 0.2f, 1f) : new Vector4(0.8f, 0.8f, 0.8f, 1f);
                    ImGui.TextColored(signalColor, flip.Reason);

                    ImGui.TableNextColumn();
                    if (flip.IsReadyToSell && ImGui.SmallButton($"Mark Sold##mark_{flip.ItemId}"))
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
                    if (ImGui.SmallButton($"Cancel##activeCancel_{flip.ItemId}"))
                    {
                        sellSuggestions.CancelFlip(flip.ItemId);
                    }
                }

                ImGui.EndTable();
            }
        }

        public void Dispose() { }
    }
}
