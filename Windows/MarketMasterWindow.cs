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
        private enum MainTab { Dashboard, Scanner, Inventory, Settings }

        private readonly MarketAssistantPlugin plugin;
        private readonly ProfitScannerService scanner;
        private readonly Configuration config;
        private readonly RetainerPriceService retainerPriceService;

        private MainTab selectedTab = MainTab.Dashboard;
        private string itemSearchQuery = string.Empty;
        private List<ItemLookup> searchResults = new();
        private List<ArbitrageOpportunity> latestResults = new();
        private List<WatchedItem> cachedWatchlist = new();
        private uint inventorySelectedItemId;
        private uint inventorySuggestedPrice;
        private bool scanRunning;
        private string scannerStatus = "Idle";
        
        // Full-market scan mode
        private ScanMode currentScanMode = ScanMode.Watchlist;
        private int topItemsCountUI = 200;
        
        // Copy feedback
        private DateTime lastCopyTime = DateTime.MinValue;
        private DateTime lastAutoFillTime = DateTime.MinValue;
        private string inventoryStatus = string.Empty;

        public MarketMasterWindow(MarketAssistantPlugin plugin, ProfitScannerService scanner)
            : base("Market Master Pro###MarketMasterProWindow")
        {
            this.plugin = plugin;
            this.scanner = scanner;
            this.config = plugin.Configuration;
            this.retainerPriceService = plugin.RetainerPriceService;
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
            // Force white text — the FFXIV Dalamud theme sets ImGuiCol.Text to a dark colour that
            // blends into dark window/button backgrounds, making plain Text() and Button labels invisible.
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            DrawHeader();
            ImGui.Separator();

            ImGui.BeginChild("##sidebar", new Vector2(180, -1), true);
            DrawTabButton(MainTab.Dashboard, "Dashboard");
            DrawTabButton(MainTab.Scanner, "Scanner");
            DrawTabButton(MainTab.Inventory, "Inventory");
            DrawTabButton(MainTab.Settings, "Settings");
            ImGui.EndChild();

            ImGui.SameLine();

            // EndChild is called unconditionally so ImGui stack stays clean even if the tab throws
            ImGui.BeginChild("##content", new Vector2(0, -1), false);
            try
            {
                switch (selectedTab)
                {
                    case MainTab.Dashboard:  DrawDashboardTab();  break;
                    case MainTab.Scanner:    DrawScannerTab();    break;
                    case MainTab.Inventory:  DrawInventoryTab();  break;
                    case MainTab.Settings:   DrawSettingsTab();   break;
                }
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), $"Tab error: {ex.Message}");
            }
            ImGui.EndChild();
            ImGui.PopStyleColor(); // paired with PushStyleColor(Text, white) at top of Draw()
        }

        private void DrawHeader()
        {
            var health = scanner.GetApiHealth();
            var color = health.SafeZone switch
            {
                "Green"  => new Vector4(0.30f, 0.95f, 0.35f, 1f),
                "Yellow" => new Vector4(0.95f, 0.85f, 0.25f, 1f),
                _        => new Vector4(0.95f, 0.35f, 0.35f, 1f)
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
                var min  = series.Min(s => s.TotalNetProfit);
                var max  = series.Max(s => s.TotalNetProfit);
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
            DrawOpportunityTable(latestResults, "##dashOppTable", 7);
        }

        private void DrawScannerTab()
        {
            ImGui.Text("Profit Scanner");
            ImGui.TextDisabled("Cross-checks Home World min price against DC low price with tax and velocity filters.");
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
            if (ImGui.Button("Refresh List"))
                RefreshWatchlist();
            ImGui.SameLine();
            ImGui.TextDisabled(scannerStatus);

            ImGui.Spacing();
            ImGui.Text("Scan Mode:");
            ImGui.RadioButton("Watchlist Only", ref currentScanMode, ScanMode.Watchlist);
            ImGui.SameLine();
            ImGui.RadioButton("Top Items", ref currentScanMode, ScanMode.TopItems);
            ImGui.SameLine();
            ImGui.RadioButton("High Velocity", ref currentScanMode, ScanMode.VelocityThreshold);
            
            if (currentScanMode == ScanMode.TopItems)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(120);
                ImGui.InputInt("##topCount", ref topItemsCountUI);
                if (topItemsCountUI < 10) topItemsCountUI = 10;
                if (topItemsCountUI > 5000) topItemsCountUI = 5000;
            }

            ImGui.Spacing();
            ImGui.Separator();

            // Left panel — always close EndChild even if content throws
            ImGui.BeginChild("##scannerLeft", new Vector2(430, 0), true);
            try { DrawScannerLeftPanel(); }
            catch (Exception ex) { ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), $"Error: {ex.Message}"); }
            ImGui.EndChild();

            ImGui.SameLine();

            // Right panel
            ImGui.BeginChild("##scannerRight", new Vector2(0, 0), true);
            try { DrawScannerRightPanel(); }
            catch (Exception ex) { ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), $"Error: {ex.Message}"); }
            ImGui.EndChild();
        }

        private void DrawScannerLeftPanel()
        {
            ImGui.Text("Search Results");
            ImGui.Separator();
            if (ImGui.BeginTable("##searchTable", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 260)))
            {
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("ID",     ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableHeadersRow();
                foreach (var item in searchResults)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Name);
                    ImGui.TableNextColumn(); ImGui.Text(item.ItemId.ToString());
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"Watch##w{item.ItemId}"))
                    {
                        scanner.AddWatchItem(item);
                        RefreshWatchlist();
                    }
                }
                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Text("Watchlist");
            ImGui.Separator();
            if (ImGui.BeginTable("##watchTable", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 220)))
            {
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("ID",     ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableHeadersRow();
                foreach (var watched in cachedWatchlist)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(watched.Name);
                    ImGui.TableNextColumn(); ImGui.Text(watched.ItemId.ToString());
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"Remove##r{watched.ItemId}"))
                    {
                        scanner.RemoveWatchItem(watched.ItemId);
                        RefreshWatchlist();
                    }
                }
                ImGui.EndTable();
            }
        }

        private void DrawScannerRightPanel()
        {
            ImGui.Text("Opportunities");
            ImGui.Separator();
            latestResults = scanner.GetLastResults().ToList();
            if (latestResults.Count == 0)
                ImGui.TextDisabled("No opportunities yet. Add items to the watchlist and click Run Scan.");
            else
                DrawOpportunityTable(latestResults, "##scannerOppTable", 9);
        }

        private void DrawInventoryTab()
        {
            ImGui.Text("Manual Price Helper");
            ImGui.TextDisabled("Fetch the current Home World floor and copy a suggested undercut price.");
            if (cachedWatchlist.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Add items to the watchlist in the Scanner tab first.");
                return;
            }
            var labels = cachedWatchlist.Select(w => $"{w.Name} ({w.ItemId})").ToArray();
            var selectedIndex = 0;
            if (inventorySelectedItemId != 0)
            {
                var idx = cachedWatchlist.FindIndex(w => w.ItemId == inventorySelectedItemId);
                if (idx >= 0) selectedIndex = idx;
            }
            if (ImGui.Combo("Tracked item", ref selectedIndex, labels, labels.Length))
                inventorySelectedItemId = cachedWatchlist[selectedIndex].ItemId;
            else if (inventorySelectedItemId == 0 && cachedWatchlist.Count > 0)
                inventorySelectedItemId = cachedWatchlist[0].ItemId;

            ImGui.Spacing();
            if (ImGui.Button("Fetch Current Home Floor"))
                _ = RefreshSuggestedPriceAsync(inventorySelectedItemId);
            ImGui.SameLine();
            if (inventorySuggestedPrice > 0 && ImGui.Button("Copy Suggested Price"))
            {
                ImGui.SetClipboardText(inventorySuggestedPrice.ToString());
                lastCopyTime = DateTime.Now;
                inventoryStatus = "Copied suggested price to clipboard";
            }

            if (config.EnableRetainerAutoFill)
            {
                ImGui.SameLine();
                var retainerWindowDetected = inventorySuggestedPrice > 0 && retainerPriceService.IsRetainerSellWindowOpen();
                ImGui.BeginDisabled(!retainerWindowDetected);
                if (ImGui.Button("Auto-Fill Retainer Price"))
                {
                    if (retainerPriceService.TryAutoFillPrice(inventorySuggestedPrice, out var status))
                        lastAutoFillTime = DateTime.Now;
                    inventoryStatus = status;
                }
                ImGui.EndDisabled();
            }
            ImGui.Spacing();
            
            var timeSinceCopy = DateTime.Now - lastCopyTime;
            var timeSinceAutoFill = DateTime.Now - lastAutoFillTime;
            if (timeSinceCopy.TotalSeconds < 2)
            {
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "✓ Copied to clipboard!");
            }
            else if (timeSinceAutoFill.TotalSeconds < 2)
            {
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "✓ Auto-filled retainer price!");
            }
            else if (inventorySuggestedPrice > 0)
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f),
                    $"Suggested listing price: {inventorySuggestedPrice:N0} gil");
            else
                ImGui.TextDisabled("No suggestion yet. Fetch floor first.");

            if (!string.IsNullOrWhiteSpace(inventoryStatus))
                ImGui.TextDisabled(inventoryStatus);
            
            ImGui.TextDisabled("(Paste the price into your Retainer's listing interface)");
            if (config.EnableRetainerAutoFill && !retainerPriceService.IsRetainerSellWindowOpen())
                ImGui.TextDisabled("Open the Retainer sell price window to enable auto-fill.");
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

            var autoFill = config.EnableRetainerAutoFill;
            if (ImGui.Checkbox("Enable retainer auto-fill button", ref autoFill))
                config.EnableRetainerAutoFill = autoFill;

            if (ImGui.Button("Save Settings"))
            {
                config.Save();
                plugin.RefreshBackgroundPolling();
                scannerStatus = "Settings saved";
            }
        }

        private void DrawOpportunityTable(List<ArbitrageOpportunity> opportunities,
            string tableId, int visibleColumns)
        {
            var columnCount = Math.Max(visibleColumns, 7);
            if (!ImGui.BeginTable(tableId, columnCount,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 0)))
                return;

            ImGui.TableSetupColumn("Item");
            ImGui.TableSetupColumn("Home Min",  ImGuiTableColumnFlags.WidthFixed, 92);
            ImGui.TableSetupColumn("DC Low",    ImGuiTableColumnFlags.WidthFixed, 92);
            ImGui.TableSetupColumn("Net",       ImGuiTableColumnFlags.WidthFixed, 92);
            ImGui.TableSetupColumn("Profit %",  ImGuiTableColumnFlags.WidthFixed, 82);
            ImGui.TableSetupColumn("Sales/day", ImGuiTableColumnFlags.WidthFixed, 82);
            ImGui.TableSetupColumn("Safe Qty",  ImGuiTableColumnFlags.WidthFixed, 72);
            if (visibleColumns >= 8)
                ImGui.TableSetupColumn("Bot Flag", ImGuiTableColumnFlags.WidthFixed, 72);
            if (visibleColumns >= 9)
                ImGui.TableSetupColumn("Scanned",  ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableHeadersRow();

            foreach (var opp in opportunities)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(opp.ItemName);
                ImGui.TableNextColumn(); ImGui.Text(opp.HomeWorldMinPrice.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.Text(opp.DataCenterLowestPrice.ToString("N0"));

                ImGui.TableNextColumn();
                var netColor = opp.NetProfitPerUnit >= 0
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : new Vector4(1f, 0.4f, 0.4f, 1f);
                ImGui.TextColored(netColor, opp.NetProfitPerUnit.ToString("N0"));

                ImGui.TableNextColumn(); ImGui.Text($"{opp.ProfitPercent:F1}%");
                ImGui.TableNextColumn(); ImGui.Text($"{opp.SaleVelocityPerDay:F1}");

                ImGui.TableNextColumn();
                var qtyColor = opp.SafeBuyQty >= 3
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : opp.SafeBuyQty == 2
                        ? new Vector4(0.95f, 0.85f, 0.25f, 1f)
                        : new Vector4(0.95f, 0.5f, 0.2f, 1f);
                ImGui.TextColored(qtyColor, opp.SafeBuyQty.ToString());

                if (visibleColumns >= 8)
                {
                    ImGui.TableNextColumn();
                    if (opp.PotentialBotSellerPattern)
                        ImGui.TextColored(new Vector4(0.95f, 0.5f, 0.2f, 1f), "Yes");
                    else
                        ImGui.TextDisabled("No");
                }

                if (visibleColumns >= 9)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text(opp.ScannedUtc.ToLocalTime().ToString("HH:mm:ss"));
                }
            }

            ImGui.EndTable();
        }

        private void RefreshWatchlist()
        {
            try { cachedWatchlist = scanner.GetWatchlist().ToList(); }
            catch { cachedWatchlist = new List<WatchedItem>(); }
        }

        private async Task RunScanAsync()
        {
            scanRunning = true;
            scannerStatus = "Setting up scan...";
            
            // Set scan mode on the service
            scanner.SetScanMode(currentScanMode, null, topItemsCountUI);
            
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
            // Capture push state BEFORE the button call — clicking the button mutates selectedTab,
            // which would cause the Pop check to fire even when nothing was pushed, crashing the game.
            var pushed = selectedTab == tab;
            if (pushed)
            {
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.25f, 0.4f, 0.8f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.4f, 0.8f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.25f, 0.4f, 0.8f, 0.9f));
            }
            if (ImGui.Button(label, new Vector2(-1, 36)))
                selectedTab = tab;
            if (pushed)
                ImGui.PopStyleColor(3);
        }

        public void Dispose() { }
    }
}
