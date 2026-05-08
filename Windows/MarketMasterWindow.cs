using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using UndercutterFFXIV.Models;
using UndercutterFFXIV.Services;

namespace UndercutterFFXIV.Windows
{
    public sealed class MarketMasterWindow : Window, IDisposable
    {
        private enum MainTab
        {
            Dashboard,
            Scanner,
            Inventory,
            Settings
        }

        private readonly MarketAssistantPlugin plugin;
        private readonly ProfitScannerService scanner;
        private readonly Configuration config;

        private MainTab selectedTab = MainTab.Dashboard;
        private string itemSearchQuery = string.Empty;
        private List<ItemLookup> searchResults = new();
        private List<ArbitrageOpportunity> latestResults = new();
        private uint inventorySelectedItemId;
        private uint inventorySuggestedPrice;
        private bool scanRunning;
        private string scannerStatus = "Idle";

        public MarketMasterWindow(MarketAssistantPlugin plugin, ProfitScannerService scanner)
            : base("Market Master Pro###MarketMasterProWindow")
        {
            this.plugin = plugin;
            this.scanner = scanner;
            this.config = plugin.Configuration;

            Size = new Vector2(1120, 700);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public void OpenScanner()
        {
            selectedTab = MainTab.Scanner;
            IsOpen = true;
        }

        public override void Draw()
        {
            DrawHeader();
            ImGui.Separator();

            ImGui.BeginChild("##sidebar", new Vector2(180, -1), true);
            DrawTabButton(MainTab.Dashboard, "Dashboard");
            DrawTabButton(MainTab.Scanner, "Scanner");
            DrawTabButton(MainTab.Inventory, "Inventory");
            DrawTabButton(MainTab.Settings, "Settings");
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.BeginChild("##content", new Vector2(0, -1), false);
            switch (selectedTab)
            {
                case MainTab.Dashboard:
                    DrawDashboardTab();
                    break;
                case MainTab.Scanner:
                    DrawScannerTab();
                    break;
                case MainTab.Inventory:
                    DrawInventoryTab();
                    break;
                case MainTab.Settings:
                    DrawSettingsTab();
                    break;
            }
            ImGui.EndChild();
        }

        private void DrawHeader()
        {
            var health = scanner.GetApiHealth();
            var color = health.SafeZone switch
            {
                "Green" => new Vector4(0.30f, 0.95f, 0.35f, 1f),
                "Yellow" => new Vector4(0.95f, 0.85f, 0.25f, 1f),
                _ => new Vector4(0.95f, 0.35f, 0.35f, 1f)
            };

            ImGui.Text("Market Master Pro");
            ImGui.SameLine();
            ImGui.TextDisabled($"Home: {config.WorldName} | DC: {config.DataCenterName}");
            ImGui.SameLine();
            ImGui.TextColored(color, $"Safe Zone: {health.SafeZone}");

            ImGui.TextDisabled($"API avg latency: {health.AverageLatencyMs:F0} ms | error rate: {health.ErrorRate:P0}");
        }

        private void DrawDashboardTab()
        {
            ImGui.Text("Profit Overview");
            var series = scanner.GetProfitSeries(30);
            if (series.Count > 1)
            {
                var min = series.Min(s => s.TotalNetProfit);
                var max = series.Max(s => s.TotalNetProfit);
                var last = series.Last().TotalNetProfit;
                ImGui.Text($"30d min/max net: {min:N0} / {max:N0} gil");
                ImGui.Text($"Latest daily net snapshot: {last:N0} gil");
            }
            else
            {
                ImGui.TextDisabled("No historical scan data yet.");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Latest Opportunities");

            latestResults = scanner.GetLastResults().Take(40).ToList();
            if (latestResults.Count == 0)
            {
                ImGui.TextDisabled("Run a scan from the Scanner tab to populate opportunities.");
                return;
            }

            DrawOpportunityTable(latestResults, "##dashOppTable", 6);
        }

        private void DrawScannerTab()
        {
            ImGui.Text("Profit Scanner");
            ImGui.TextDisabled("Cross-checks Home World min price against Data Centre low price with tax and velocity filters.");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(380);
            ImGui.InputText("Item search", ref itemSearchQuery, 128);
            ImGui.SameLine();
            if (ImGui.Button("Find Items"))
                searchResults = scanner.SearchItems(itemSearchQuery, 100).ToList();

            ImGui.SameLine();
            if (ImGui.Button("Run Scan") && !scanRunning)
                _ = RunScanAsync();

            ImGui.SameLine();
            ImGui.TextDisabled(scannerStatus);

            ImGui.Spacing();
            ImGui.Separator();

            if (ImGui.BeginChild("##scannerLeft", new Vector2(430, 0), true))
            {
                ImGui.Text("Search Results");
                ImGui.Separator();

                if (ImGui.BeginTable("##searchTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 260)))
                {
                    ImGui.TableSetupColumn("Name");
                    ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableHeadersRow();

                    foreach (var item in searchResults)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(item.Name);
                        ImGui.TableNextColumn();
                        ImGui.Text(item.ItemId.ToString());
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"Watch##watch{item.ItemId}"))
                            scanner.AddWatchItem(item);
                    }

                    ImGui.EndTable();
                }

                ImGui.Spacing();
                ImGui.Text("Watchlist");
                ImGui.Separator();

                var watchlist = scanner.GetWatchlist();
                if (ImGui.BeginTable("##watchTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 220)))
                {
                    ImGui.TableSetupColumn("Name");
                    ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableHeadersRow();

                    foreach (var watched in watchlist)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(watched.Name);
                        ImGui.TableNextColumn();
                        ImGui.Text(watched.ItemId.ToString());
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"Remove##remove{watched.ItemId}"))
                            scanner.RemoveWatchItem(watched.ItemId);
                    }

                    ImGui.EndTable();
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("##scannerRight", new Vector2(0, 0), true))
            {
                ImGui.Text("Opportunities");
                ImGui.Separator();

                latestResults = scanner.GetLastResults().ToList();
                if (latestResults.Count == 0)
                    ImGui.TextDisabled("No opportunities yet.");
                else
                    DrawOpportunityTable(latestResults, "##scannerOppTable", 8);
            }
            ImGui.EndChild();
        }

        private void DrawInventoryTab()
        {
            ImGui.Text("Manual Price Helper");
            ImGui.TextDisabled("Manual-only workflow: fetch floor price and copy suggested undercut price.");

            var watch = scanner.GetWatchlist();
            if (watch.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Add items to watchlist in Scanner first.");
                return;
            }

            var labels = watch.Select(w => $"{w.Name} ({w.ItemId})").ToArray();
            var selectedIndex = 0;
            if (inventorySelectedItemId != 0)
            {
                selectedIndex = watch.ToList().FindIndex(w => w.ItemId == inventorySelectedItemId);
                if (selectedIndex < 0) selectedIndex = 0;
            }

            if (ImGui.Combo("Tracked item", ref selectedIndex, labels, labels.Length))
                inventorySelectedItemId = watch[selectedIndex].ItemId;
            else if (inventorySelectedItemId == 0)
                inventorySelectedItemId = watch[0].ItemId;

            ImGui.Spacing();
            if (ImGui.Button("Fetch Current Home Floor"))
                _ = RefreshSuggestedPriceAsync(inventorySelectedItemId);

            ImGui.SameLine();
            if (inventorySuggestedPrice > 0 && ImGui.Button("Copy Suggested Price"))
                ImGui.SetClipboardText(inventorySuggestedPrice.ToString());

            ImGui.Spacing();
            if (inventorySuggestedPrice > 0)
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"Suggested listing price: {inventorySuggestedPrice:N0} gil");
            else
                ImGui.TextDisabled("No suggestion yet. Fetch floor first.");
        }

        private void DrawSettingsTab()
        {
            ImGui.Text("Scanner Settings");
            ImGui.Separator();

            var world = config.WorldName;
            ImGui.SetNextItemWidth(260);
            if (ImGui.InputText("Home World", ref world, 64))
                config.WorldName = world;

            var dc = config.DataCenterName;
            ImGui.SetNextItemWidth(260);
            if (ImGui.InputText("Data Centre", ref dc, 64))
                config.DataCenterName = dc;

            var tax = (float)config.MarketTaxRatePercent;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Tax %", ref tax, 0, 10, "%.1f%%"))
                config.MarketTaxRatePercent = tax;

            var velocity = (float)config.MinSaleVelocityPerDay;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Min sales/day", ref velocity, 0, 10, "%.1f"))
                config.MinSaleVelocityPerDay = velocity;

            var minGil = config.MinNetProfitGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Min net profit (gil)", ref minGil))
                config.MinNetProfitGil = Math.Max(0, minGil);

            var minPct = (float)config.MinNetProfitPercent;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Min profit %", ref minPct, 0, 100, "%.1f%%"))
                config.MinNetProfitPercent = minPct;

            var lookback = config.ScannerLookbackDays;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Velocity lookback days", ref lookback))
                config.ScannerLookbackDays = Math.Clamp(lookback, 1, 30);

            var bg = config.EnableBackgroundPolling;
            if (ImGui.Checkbox("Enable background scanner polling", ref bg))
                config.EnableBackgroundPolling = bg;

            var poll = config.PollingBaseSeconds;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Polling interval (sec)", ref poll))
                config.PollingBaseSeconds = Math.Max(30, poll);

            var jitter = config.PollingJitterSeconds;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Polling jitter (+/- sec)", ref jitter))
                config.PollingJitterSeconds = Math.Clamp(jitter, 0, 120);

            if (ImGui.Button("Save Settings"))
            {
                config.Save();
                plugin.RefreshBackgroundPolling();
                scannerStatus = "Settings saved";
            }
        }

        private void DrawOpportunityTable(List<ArbitrageOpportunity> opportunities, string tableId, int visibleColumns)
        {
            if (!ImGui.BeginTable(tableId, visibleColumns, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 0)))
                return;

            ImGui.TableSetupColumn("Item");
            ImGui.TableSetupColumn("Home Min", ImGuiTableColumnFlags.WidthFixed, 92);
            ImGui.TableSetupColumn("DC Low", ImGuiTableColumnFlags.WidthFixed, 92);
            ImGui.TableSetupColumn("Net", ImGuiTableColumnFlags.WidthFixed, 92);
            ImGui.TableSetupColumn("Profit %", ImGuiTableColumnFlags.WidthFixed, 82);
            ImGui.TableSetupColumn("Sales/day", ImGuiTableColumnFlags.WidthFixed, 82);
            if (visibleColumns >= 7)
                ImGui.TableSetupColumn("Bot Flag", ImGuiTableColumnFlags.WidthFixed, 72);
            if (visibleColumns >= 8)
                ImGui.TableSetupColumn("Scanned", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableHeadersRow();

            foreach (var opportunity in opportunities)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(opportunity.ItemName);

                ImGui.TableNextColumn();
                ImGui.Text(opportunity.HomeWorldMinPrice.ToString("N0"));

                ImGui.TableNextColumn();
                ImGui.Text(opportunity.DataCenterLowestPrice.ToString("N0"));

                ImGui.TableNextColumn();
                var netColor = opportunity.NetProfitPerUnit >= 0
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : new Vector4(1f, 0.4f, 0.4f, 1f);
                ImGui.TextColored(netColor, opportunity.NetProfitPerUnit.ToString("N0"));

                ImGui.TableNextColumn();
                ImGui.Text($"{opportunity.ProfitPercent:F1}%");

                ImGui.TableNextColumn();
                ImGui.Text($"{opportunity.SaleVelocityPerDay:F1}");

                if (visibleColumns >= 7)
                {
                    ImGui.TableNextColumn();
                    if (opportunity.PotentialBotSellerPattern)
                        ImGui.TextColored(new Vector4(0.95f, 0.5f, 0.2f, 1f), "Yes");
                    else
                        ImGui.TextDisabled("No");
                }

                if (visibleColumns >= 8)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text(opportunity.ScannedUtc.ToLocalTime().ToString("HH:mm:ss"));
                }
            }

            ImGui.EndTable();
        }

        private async Task RunScanAsync()
        {
            scanRunning = true;
            scannerStatus = "Scanning watchlist...";
            try
            {
                var results = await scanner.ScanWatchlistAsync(CancellationToken.None);
                latestResults = results.ToList();
                scannerStatus = $"Scan complete: {latestResults.Count} opportunities";
            }
            catch (Exception ex)
            {
                scannerStatus = $"Scan failed: {ex.Message}";
            }
            finally
            {
                scanRunning = false;
            }
        }

        private async Task RefreshSuggestedPriceAsync(uint itemId)
        {
            try
            {
                var floor = await scanner.FetchHomeFloorPriceAsync(itemId, CancellationToken.None);
                if (floor == 0)
                {
                    scannerStatus = "Could not fetch floor price";
                    inventorySuggestedPrice = 0;
                    return;
                }

                inventorySuggestedPrice = floor > config.UndercutAmount
                    ? floor - config.UndercutAmount
                    : floor;
                scannerStatus = "Floor price updated";
            }
            catch (Exception ex)
            {
                scannerStatus = $"Floor fetch failed: {ex.Message}";
            }
        }

        private void DrawTabButton(MainTab tab, string label)
        {
            if (selectedTab == tab)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.4f, 0.8f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.4f, 0.8f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.4f, 0.8f, 0.9f));
            }

            if (ImGui.Button(label, new Vector2(-1, 36)))
                selectedTab = tab;

            if (selectedTab == tab)
                ImGui.PopStyleColor(3);
        }

        public void Dispose()
        {
        }
    }
}
