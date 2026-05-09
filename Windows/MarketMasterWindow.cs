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
        private const int InventoryLookupConcurrency = 6;

        private enum MainTab { Dashboard, Scanner, Inventory, History, Settings }

        private readonly MarketAssistantPlugin plugin;
        private readonly ProfitScannerService scanner;
        private readonly Configuration config;
        private readonly RetainerPriceService retainerPriceService;

        private MainTab selectedTab = MainTab.Dashboard;
        private string itemSearchQuery = string.Empty;
        private List<ItemLookup> searchResults = new();
        private List<ArbitrageOpportunity> latestResults = new();
        private List<WatchedItem> cachedWatchlist = new();
        private List<InventoryGridRow> inventoryRows = new();
        private readonly List<InventoryGridRow> repriceQueue = new();
        private readonly HashSet<string> repriceQueueKeys = new(StringComparer.Ordinal);
        private bool scanRunning;
        private CancellationTokenSource? currentScanCancellation;
        private string scannerStatus = "Idle";
        private bool inventoryGridRefreshInProgress;
        private bool inventoryGridInitialized;
        
        // Full-market scan mode
        private ScanMode currentScanMode = ScanMode.TopItems;
        private int topItemsCountUI = 250;
        
        // Copy feedback
        private DateTime lastCopyTime = DateTime.MinValue;
        private DateTime lastAutoFillTime = DateTime.MinValue;
        private DateTime lastSettingsSaveUtc = DateTime.MinValue;
        private bool pendingSettingsSave;
        private string inventoryStatus = string.Empty;
        private string historyStatus = string.Empty;
        private int historyDaysFilter = 30;
        private int historyItemIdInput;
        private string historyItemNameInput = string.Empty;
        private int historyBuyPriceInput;
        private int historySellPriceInput;
        private int historyQuantityInput = 1;
        private readonly Dictionary<ulong, string> pendingBuyNameEdits = new();
        private readonly Dictionary<ulong, int> pendingBuyBuyPriceEdits = new();
        private readonly Dictionary<ulong, int> pendingBuySellPriceEdits = new();
        private readonly Dictionary<ulong, int> pendingBuyQtyEdits = new();

        private sealed class InventoryGridRow
        {
            public int SlotIndex { get; init; }
            public uint ItemId { get; init; }
            public string ItemName { get; init; } = string.Empty;
            public uint CurrentSellPrice { get; init; }
            public uint UndercutPrice { get; init; }
            public bool IsUndercut { get; init; }
            public int OwnedQuantity { get; init; }
            public int FillRatePercent { get; init; }
            public string PriceAction { get; init; } = "Hold";
            public bool IsLowTrust { get; init; }
            public string TrustReason { get; init; } = string.Empty;
        }

        private enum SortDirection { Ascending, Descending }
        private SortDirection opportunitySortDirection = SortDirection.Descending;

        private static string BuildWindowTitle()
        {
            var version = typeof(MarketMasterWindow).Assembly.GetName().Version;
            var versionText = version != null ? version.ToString(3) : "dev";
            return $"Market Master Pro v{versionText}###MarketMasterProWindow";
        }

        private static float CalculateColumnWidth(
            IEnumerable<string> values,
            string header,
            float minWidth,
            float maxWidth,
            int sampleLimit = 120)
        {
            var width = ImGui.CalcTextSize(header).X + 24f;
            var sampled = 0;

            foreach (var value in values)
            {
                if (sampled++ >= sampleLimit)
                    break;

                var text = string.IsNullOrEmpty(value) ? "-" : value;
                var textWidth = ImGui.CalcTextSize(text).X + 24f;
                if (textWidth > width)
                    width = textWidth;
            }

            return Math.Clamp(width, minWidth, maxWidth);
        }

        public MarketMasterWindow(MarketAssistantPlugin plugin, ProfitScannerService scanner)
            : base(BuildWindowTitle())
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
            OnOpen();
            selectedTab = MainTab.Scanner;
            IsOpen = true;
        }

        public override void OnOpen()
        {
            RefreshWatchlist();
            latestResults = scanner.GetLastResults().ToList();
            _ = RefreshInventoryGridAsync();
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
            DrawTabButton(MainTab.History, "History");
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
                    case MainTab.History:    DrawHistoryTab();    break;
                    case MainTab.Settings:   DrawSettingsTab();   break;
                }
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), $"Tab error: {ex.Message}");
            }
            ImGui.EndChild();
            FlushPendingSettingsSave();
            ImGui.PopStyleColor(); // paired with PushStyleColor(Text, white) at top of Draw()
        }

        private void MarkSettingsDirty()
        {
            pendingSettingsSave = true;
        }

        private void FlushPendingSettingsSave()
        {
            if (!pendingSettingsSave)
                return;

            var now = DateTime.UtcNow;
            if ((now - lastSettingsSaveUtc).TotalMilliseconds < 500)
                return;

            config.Save();
            plugin.RefreshBackgroundPolling();
            pendingSettingsSave = false;
            lastSettingsSaveUtc = now;
            scannerStatus = "Settings auto-saved";
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
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Green/Yellow/Red health based on API latency, error rate, and recent successful requests.");
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
            DrawOpportunityTable(latestResults, "##dashOppTable");
        }

        private void DrawScannerTab()
        {
            ImGui.Text("Profit Scanner");
            ImGui.TextDisabled("Cross-checks Home World min price against DC low price with tax and velocity filters.");
            if (config.AutoTrackCurrentlySellingItems)
            {
                var liveTrackedCount = retainerPriceService.GetCurrentSellingItems().Count;
                if (liveTrackedCount > 0)
                    ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"Auto-tracking {liveTrackedCount} items currently listed on retainers.");
                else
                    ImGui.TextDisabled("Auto-track is enabled, but no live retainer listings are loaded right now.");
                ImGui.TextDisabled("Watchlist mode also includes items currently listed on your retainers.");
            }
            ImGui.Spacing();

            ImGui.SetNextItemWidth(380);
            ImGui.InputText("Item search", ref itemSearchQuery, 128);
            ImGui.SameLine();
            if (ImGui.Button("Find Items"))
                searchResults = scanner.SearchItems(itemSearchQuery, 100).ToList();
            ImGui.SameLine();
            if (ImGui.Button("Run Scan") && !scanRunning)
                _ = RunScanAsync();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Starts a manual scan using the selected mode and updates opportunities while it runs.");
            ImGui.SameLine();
            ImGui.BeginDisabled(!scanRunning || currentScanCancellation == null);
            if (ImGui.Button("Cancel Scan"))
            {
                scannerStatus = "Cancelling scan...";
                currentScanCancellation?.Cancel();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stops the current manual scan.");
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Refresh List"))
                RefreshWatchlist();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Refreshes watchlist and live retainer-tracked items.");
            ImGui.SameLine();
            ImGui.TextDisabled(scannerStatus);

            var progress = scanner.GetScanProgress();
            if (progress.IsRunning && progress.Total > 0)
            {
                var pct = Math.Clamp(progress.Percent / 100f, 0f, 1f);
                ImGui.Spacing();
                ImGui.ProgressBar(pct, new Vector2(-1, 0), $"Scanning {progress.Processed}/{progress.Total} ({pct * 100f:F0}%)");
            }

            var timing = scanner.GetLastScanTiming();
            if (timing.TotalMs > 0)
            {
                ImGui.TextDisabled($"Last scan: {timing.TotalMs} ms total | Home fetch {timing.HomeFetchMs} ms | Home filter {timing.HomeFilterMs} ms | DC fetch {timing.DcFetchMs} ms | Evaluate {timing.EvaluationMs} ms | Candidates {timing.CandidateCount}");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Timing for the most recent manual/background scan pipeline.");
            }

            ImGui.Spacing();
            ImGui.Text("Scan Mode:");
            ImGui.RadioButton("Watchlist Only", ref currentScanMode, ScanMode.Watchlist);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scans only your watched items (manual + auto-tracked live listings).");
            ImGui.SameLine();
            ImGui.RadioButton("Top Items", ref currentScanMode, ScanMode.TopItems);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scans watched items plus a broad sample of marketable items.");
            ImGui.SameLine();
            ImGui.RadioButton("High Velocity", ref currentScanMode, ScanMode.VelocityThreshold);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scans watched items plus a broader market-focused sample.");
            ImGui.SameLine();
            ImGui.RadioButton("Gear Only", ref currentScanMode, ScanMode.GearOnly);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scans equippable gear only (weapons, armor, and accessories).");
            ImGui.SameLine();
            ImGui.RadioButton("Weapons", ref currentScanMode, ScanMode.WeaponsOnly);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scans weapon opportunities only.");
            ImGui.SameLine();
            ImGui.RadioButton("Armor", ref currentScanMode, ScanMode.ArmorOnly);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scans armor opportunities only.");
            ImGui.SameLine();
            ImGui.RadioButton("Accessories", ref currentScanMode, ScanMode.AccessoriesOnly);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scans jewelry/accessory opportunities only.");
            ImGui.SameLine();
            ImGui.RadioButton("Consumables", ref currentScanMode, ScanMode.ConsumablesOnly);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Scans consumable items only (potions, food, materials, etc.).");
            
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
            ImGui.BeginChild("##scannerLeft", new Vector2(470, 0), true);
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
            var leftPanelWidth = Math.Max(220f, ImGui.GetContentRegionAvail().X);
            var searchNameWidth = CalculateColumnWidth(
                searchResults.Select(item => item.Name),
                "Name",
                160f,
                Math.Max(220f, leftPanelWidth * 1.35f));
            var watchNameWidth = CalculateColumnWidth(
                cachedWatchlist.Select(item => item.Name),
                "Name",
                170f,
                Math.Max(230f, leftPanelWidth * 1.4f));

            ImGui.Text("Search Results");
            ImGui.Separator();
            if (ImGui.BeginTable("##searchTable", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0, 260)))
            {
                ImGui.TableSetupColumn("Name",   ImGuiTableColumnFlags.WidthFixed, searchNameWidth);
                ImGui.TableSetupColumn("Req Lv", ImGuiTableColumnFlags.WidthFixed, 44);
                ImGui.TableSetupColumn("iLvl",   ImGuiTableColumnFlags.WidthFixed, 44);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableHeadersRow();
                foreach (var item in searchResults)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Name);
                    ImGui.TableNextColumn(); ImGui.Text(item.IsGear ? item.RequiredLevel.ToString() : "-");
                    ImGui.TableNextColumn(); ImGui.Text(item.IsGear && item.ItemLevel > 0 ? item.ItemLevel.ToString() : "-");
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
            if (ImGui.BeginTable("##watchTable", 2,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0, 220)))
            {
                ImGui.TableSetupColumn("Name",   ImGuiTableColumnFlags.WidthFixed, watchNameWidth);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableHeadersRow();
                foreach (var watched in cachedWatchlist)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(watched.Name);
                    ImGui.TableNextColumn();
                        if (watched.IsAutoTracked)
                        {
                            ImGui.TextDisabled("Live");
                        }
                        else if (ImGui.SmallButton($"Remove##r{watched.ItemId}"))
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
                DrawOpportunityTable(latestResults, "##scannerOppTable");
        }

        private void DrawInventoryTab()
        {
            if (!inventoryGridInitialized && !inventoryGridRefreshInProgress)
                _ = RefreshInventoryGridAsync();

            ImGui.Text("Retainer Listings");
            ImGui.TextDisabled("Live rows for items currently selling. Suggested price is based on Home World lowest listed price.");

            var retainerWindowDetected = config.EnableRetainerAutoFill && retainerPriceService.IsRetainerSellWindowOpen();

            if (config.EnableRetainerAutoFill)
            {
                var detectionColor = retainerWindowDetected
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : new Vector4(0.95f, 0.75f, 0.25f, 1f);
                var detectionText = retainerWindowDetected
                    ? "Retainer window detected"
                    : "Retainer window not detected";
                ImGui.TextColored(detectionColor, detectionText);
            }

            ImGui.Spacing();
            if (ImGui.Button("Refresh Inventory Grid") && !inventoryGridRefreshInProgress)
                _ = RefreshInventoryGridAsync();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Re-reads active retainer listings and recalculates suggested prices.");
            ImGui.SameLine();
            if (inventoryGridRefreshInProgress)
                ImGui.TextDisabled("Refreshing...");

            ImGui.Spacing();
            ImGui.TextDisabled($"Queued reprices: {repriceQueue.Count}");
            ImGui.SameLine();
            ImGui.BeginDisabled(repriceQueue.Count == 0 || !(config.EnableRetainerAutoFill && retainerWindowDetected));
            if (ImGui.Button("Apply Next Queue Fill"))
            {
                var next = repriceQueue[0];
                if (retainerPriceService.TryAutoFillPrice(next.UndercutPrice, out var queueStatus))
                    lastAutoFillTime = DateTime.Now;

                inventoryStatus = $"{queueStatus} [{next.ItemName}]";
                var key = BuildRepriceKey(next.SlotIndex, next.ItemId);
                repriceQueue.RemoveAt(0);
                repriceQueueKeys.Remove(key);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(repriceQueue.Count == 0);
            if (ImGui.Button("Clear Queue"))
            {
                repriceQueue.Clear();
                repriceQueueKeys.Clear();
            }
            ImGui.EndDisabled();

            ImGui.Spacing();
            if (inventoryRows.Count == 0)
            {
                ImGui.TextDisabled("No active retainer market listings detected. Open your retainer market inventory and refresh.");
            }
            else if (ImGui.BeginTable("##inventoryGrid", 9,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 360)))
            {
                ImGui.TableSetupColumn("Item Name");
                ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Current Sell Price", ImGuiTableColumnFlags.WidthFixed, 130);
                ImGui.TableSetupColumn("Suggested New Price", ImGuiTableColumnFlags.WidthFixed, 135);
                ImGui.TableSetupColumn("Undercut?", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Price Action", ImGuiTableColumnFlags.WidthFixed, 140);
                ImGui.TableSetupColumn("Trust", ImGuiTableColumnFlags.WidthFixed, 130);
                ImGui.TableSetupColumn("Copy new price", ImGuiTableColumnFlags.WidthFixed, 110);
                ImGui.TableSetupColumn("Auto fill?", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Queue", ImGuiTableColumnFlags.WidthFixed, 85);
                ImGui.TableHeadersRow();

                foreach (var row in inventoryRows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(row.ItemName);

                    ImGui.TableNextColumn();
                    ImGui.Text(row.OwnedQuantity.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(row.CurrentSellPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.Text(row.UndercutPrice.ToString("N0"));

                    ImGui.TableNextColumn();
                    if (row.IsUndercut)
                        ImGui.TextColored(new Vector4(0.95f, 0.4f, 0.4f, 1f), "Yes");
                    else
                        ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "No");

                    ImGui.TableNextColumn();
                    var actionColor = row.PriceAction.StartsWith("Exit", StringComparison.OrdinalIgnoreCase)
                        ? new Vector4(0.95f, 0.45f, 0.45f, 1f)
                        : row.PriceAction.StartsWith("Undercut", StringComparison.OrdinalIgnoreCase)
                            ? new Vector4(0.95f, 0.85f, 0.35f, 1f)
                            : new Vector4(0.7f, 0.9f, 1f, 1f);
                    ImGui.TextColored(actionColor, row.PriceAction);

                    ImGui.TableNextColumn();
                    var trustText = row.IsLowTrust ? "Low trust" : "Healthy";
                    var trustColor = row.IsLowTrust
                        ? new Vector4(0.95f, 0.55f, 0.35f, 1f)
                        : new Vector4(0.4f, 1f, 0.4f, 1f);
                    ImGui.TextColored(trustColor, trustText);
                    if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(row.TrustReason))
                        ImGui.SetTooltip(row.TrustReason);

                    ImGui.TableNextColumn();
                    ImGui.BeginDisabled(row.UndercutPrice == 0);
                    if (ImGui.SmallButton($"Copy##copy{row.SlotIndex}_{row.ItemId}"))
                    {
                        ImGui.SetClipboardText(row.UndercutPrice.ToString());
                        lastCopyTime = DateTime.Now;
                        inventoryStatus = $"Copied {row.UndercutPrice:N0} for {row.ItemName}";
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Copies the suggested new price to your clipboard.");
                    ImGui.EndDisabled();

                    ImGui.TableNextColumn();
                    ImGui.BeginDisabled(!(config.EnableRetainerAutoFill && retainerWindowDetected && row.UndercutPrice > 0));
                    if (ImGui.SmallButton($"Fill##fill{row.SlotIndex}_{row.ItemId}"))
                    {
                        if (retainerPriceService.TryAutoFillPrice(row.UndercutPrice, out var status))
                            lastAutoFillTime = DateTime.Now;
                        inventoryStatus = status;
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Clicks Adjust Price first, then fills the retainer sell-price input (requires retainer sell window open).");
                    ImGui.EndDisabled();

                    ImGui.TableNextColumn();
                    var queueKey = BuildRepriceKey(row.SlotIndex, row.ItemId);
                    if (repriceQueueKeys.Contains(queueKey))
                    {
                        if (ImGui.SmallButton($"Unqueue##queue{row.SlotIndex}_{row.ItemId}"))
                        {
                            repriceQueue.RemoveAll(item => item.SlotIndex == row.SlotIndex && item.ItemId == row.ItemId);
                            repriceQueueKeys.Remove(queueKey);
                        }
                    }
                    else
                    {
                        ImGui.BeginDisabled(row.UndercutPrice == 0);
                        if (ImGui.SmallButton($"Queue##queue{row.SlotIndex}_{row.ItemId}"))
                        {
                            repriceQueue.Add(row);
                            repriceQueueKeys.Add(queueKey);
                        }
                        ImGui.EndDisabled();
                    }
                }

                ImGui.EndTable();
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
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "✓ Selected adjust price and filled retainer price!");
            }
            else
                ImGui.TextDisabled("Use Copy or Fill on any row to apply a new price.");

            if (!string.IsNullOrWhiteSpace(inventoryStatus))
                ImGui.TextDisabled(inventoryStatus);
            
            ImGui.TextDisabled("(Paste the price into your Retainer's listing interface)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Copy always works. Auto-fill is optional and only activates when the in-game sell window is detected.");
            if (config.EnableRetainerAutoFill && !retainerWindowDetected)
                ImGui.TextDisabled("Open the Retainer sell price window to enable auto-fill.");
        }

        private void DrawHistoryTab()
        {
            ImGui.Text("Buy/Sell History");
            ImGui.TextDisabled("Track what you bought and sold items for over time.");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Days", ref historyDaysFilter))
                historyDaysFilter = Math.Clamp(historyDaysFilter, 1, 365);

            ImGui.SameLine();
            ImGui.TextDisabled(historyStatus);

            ImGui.Separator();
            ImGui.Text("Pending Auto-Captured Buys");
            var pendingBuys = scanner.GetPendingBuyCaptures(30).ToList();
            if (pendingBuys.Count == 0)
            {
                ImGui.TextDisabled("No pending purchase captures.");
            }
            else
            {
                if (ImGui.BeginTable("##pendingBuysTable", 8,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX,
                    new Vector2(0, 200)))
                {
                    ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 95);
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 55);
                    ImGui.TableSetupColumn("Buy", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Sell", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 45);
                    ImGui.TableSetupColumn("Accept", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn("Dismiss", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableHeadersRow();

                    foreach (var pending in pendingBuys)
                    {
                        if (!pendingBuyNameEdits.ContainsKey(pending.ListingId))
                            pendingBuyNameEdits[pending.ListingId] = pending.ItemName;
                        if (!pendingBuyQtyEdits.ContainsKey(pending.ListingId))
                            pendingBuyQtyEdits[pending.ListingId] = (int)Math.Max(1, pending.Quantity);
                        if (!pendingBuyBuyPriceEdits.ContainsKey(pending.ListingId))
                            pendingBuyBuyPriceEdits[pending.ListingId] = (int)Math.Max(1, pending.UnitPrice);
                        if (!pendingBuySellPriceEdits.ContainsKey(pending.ListingId))
                            pendingBuySellPriceEdits[pending.ListingId] = (int)Math.Max(1, pending.UnitPrice);

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(pending.CapturedUtc.ToLocalTime().ToString("MM/dd HH:mm"));

                        ImGui.TableNextColumn();
                        var editName = pendingBuyNameEdits[pending.ListingId];
                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputText($"##pendingName{pending.ListingId}", ref editName, 128);
                        pendingBuyNameEdits[pending.ListingId] = editName;

                        ImGui.TableNextColumn();
                        var editQty = pendingBuyQtyEdits[pending.ListingId];
                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputInt($"##pendingQty{pending.ListingId}", ref editQty);
                        pendingBuyQtyEdits[pending.ListingId] = Math.Max(1, editQty);

                        ImGui.TableNextColumn();
                        var editBuy = pendingBuyBuyPriceEdits[pending.ListingId];
                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputInt($"##pendingBuy{pending.ListingId}", ref editBuy);
                        pendingBuyBuyPriceEdits[pending.ListingId] = Math.Max(1, editBuy);

                        ImGui.TableNextColumn();
                        var editSell = pendingBuySellPriceEdits[pending.ListingId];
                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputInt($"##pendingSell{pending.ListingId}", ref editSell);
                        pendingBuySellPriceEdits[pending.ListingId] = Math.Max(1, editSell);

                        ImGui.TableNextColumn();
                        ImGui.Text(pending.IsHq ? "Yes" : "No");

                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"Accept##pendingAccept{pending.ListingId}"))
                        {
                            var ok = scanner.ConfirmPendingBuyCapture(
                                pending.ListingId,
                                pending.ItemId,
                                pendingBuyNameEdits[pending.ListingId],
                                (uint)pendingBuyQtyEdits[pending.ListingId],
                                (uint)pendingBuyBuyPriceEdits[pending.ListingId],
                                (uint)pendingBuySellPriceEdits[pending.ListingId]);

                            historyStatus = ok
                                ? $"Accepted auto-buy capture for {pendingBuyNameEdits[pending.ListingId]}"
                                : "Unable to accept pending buy capture";

                            pendingBuyNameEdits.Remove(pending.ListingId);
                            pendingBuyQtyEdits.Remove(pending.ListingId);
                            pendingBuyBuyPriceEdits.Remove(pending.ListingId);
                            pendingBuySellPriceEdits.Remove(pending.ListingId);
                        }

                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"Dismiss##pendingDismiss{pending.ListingId}"))
                        {
                            scanner.DismissPendingBuyCapture(pending.ListingId);
                            historyStatus = "Dismissed pending auto-buy capture";
                            pendingBuyNameEdits.Remove(pending.ListingId);
                            pendingBuyQtyEdits.Remove(pending.ListingId);
                            pendingBuyBuyPriceEdits.Remove(pending.ListingId);
                            pendingBuySellPriceEdits.Remove(pending.ListingId);
                        }
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.Separator();
            ImGui.Text("Add Trade Entry");

            ImGui.SetNextItemWidth(120);
            ImGui.InputInt("Item ID", ref historyItemIdInput);
            if (historyItemIdInput < 0)
                historyItemIdInput = 0;

            ImGui.SetNextItemWidth(300);
            ImGui.InputText("Item Name", ref historyItemNameInput, 128);

            ImGui.SetNextItemWidth(140);
            ImGui.InputInt("Buy Price", ref historyBuyPriceInput);
            if (historyBuyPriceInput < 0)
                historyBuyPriceInput = 0;

            ImGui.SetNextItemWidth(140);
            ImGui.InputInt("Sell Price", ref historySellPriceInput);
            if (historySellPriceInput < 0)
                historySellPriceInput = 0;

            ImGui.SetNextItemWidth(120);
            ImGui.InputInt("Quantity", ref historyQuantityInput);
            if (historyQuantityInput < 1)
                historyQuantityInput = 1;

            var canAdd = !string.IsNullOrWhiteSpace(historyItemNameInput)
                && historyBuyPriceInput > 0
                && historySellPriceInput > 0
                && historyQuantityInput > 0;

            ImGui.BeginDisabled(!canAdd);
            if (ImGui.Button("Add History Entry"))
            {
                scanner.AddTradeHistoryEntry(
                    (uint)Math.Max(0, historyItemIdInput),
                    historyItemNameInput.Trim(),
                    (uint)historyBuyPriceInput,
                    (uint)historySellPriceInput,
                    (uint)historyQuantityInput);

                historyStatus = $"Added history entry for {historyItemNameInput.Trim()}";
                historyItemIdInput = 0;
                historyItemNameInput = string.Empty;
                historyBuyPriceInput = 0;
                historySellPriceInput = 0;
                historyQuantityInput = 1;
            }
            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();

            var entries = scanner.GetTradeHistory(historyDaysFilter).ToList();
            if (entries.Count == 0)
            {
                ImGui.TextDisabled("No buy/sell history recorded for the selected period yet.");
                return;
            }

            ImGui.Text($"Entries: {entries.Count}");

            if (!ImGui.BeginTable("##tradeHistoryTable", 8,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX,
                new Vector2(0, 0)))
                return;

            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 95);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Buy", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Sell", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Net/Unit", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Net Total", ImGuiTableColumnFlags.WidthFixed, 95);
            ImGui.TableHeadersRow();

            var taxMultiplier = 1.0 - (config.MarketTaxRatePercent / 100.0);

            foreach (var entry in entries)
            {
                var netSellPerUnit = entry.SellPrice * taxMultiplier;
                var netPerUnit = netSellPerUnit - entry.BuyPrice;
                var netTotal = netPerUnit * entry.Quantity;
                var netColor = netTotal >= 0
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : new Vector4(1f, 0.45f, 0.45f, 1f);

                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(entry.TradedUtc.ToLocalTime().ToString("MM/dd HH:mm"));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.ItemName);
                ImGui.TableNextColumn(); ImGui.Text(entry.ItemId > 0 ? entry.ItemId.ToString() : "-");
                ImGui.TableNextColumn(); ImGui.Text(entry.BuyPrice.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.Text(entry.SellPrice.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.Text(entry.Quantity.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.TextColored(netColor, netPerUnit.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.TextColored(netColor, netTotal.ToString("N0"));
            }

            ImGui.EndTable();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Advanced Analytics");

            var advanced = scanner.GetAdvancedHistoryAnalytics(historyDaysFilter);
            ImGui.Text($"Win rate: {advanced.WinRatePercent:F1}% | Avg net/hour: {advanced.AverageNetGilPerHour:N0} | Est median hold: {advanced.MedianEstimatedHoldHours:F1}h");

            if (advanced.BestCategories.Count > 0)
            {
                ImGui.Text("Best categories:");
                foreach (var category in advanced.BestCategories)
                    ImGui.BulletText($"{category.Category}: {category.NetGil:N0} gil");
            }

            if (advanced.RepeatedLossItems.Count > 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.65f, 0.45f, 1f), "Repeated loss patterns:");
                foreach (var loss in advanced.RepeatedLossItems)
                    ImGui.BulletText($"{loss.ItemName} | losses: {loss.LossCount} | net: {loss.TotalLoss:N0} gil");
            }

            var timeline = scanner.GetRetainerSnapshotAnalytics(historyDaysFilter);
            ImGui.Spacing();
            ImGui.Text("Retainer Snapshot Timeline");
            ImGui.Text($"Snapshots: {timeline.TotalSnapshots} | Undercut frequency: {timeline.UndercutFrequencyPercent:F1}% | Avg sit: {timeline.AverageSitHours:F1}h");
            if (timeline.FastestChurnItems.Count > 0)
            {
                ImGui.Text("Fastest churn items:");
                foreach (var item in timeline.FastestChurnItems)
                    ImGui.BulletText($"{item.ItemName}: {item.PriceChanges} price changes");
            }
        }

        private async Task RefreshInventoryGridAsync()
        {
            if (inventoryGridRefreshInProgress)
                return;

            inventoryGridRefreshInProgress = true;
            inventoryStatus = "Refreshing inventory listings...";
            try
            {
                var listings = retainerPriceService.GetCurrentSellingListings();
                using var throttler = new SemaphoreSlim(InventoryLookupConcurrency, InventoryLookupConcurrency);
                var rowTasks = listings.Select(async listing =>
                {
                    await throttler.WaitAsync();
                    try
                    {
                        var homeSnapshot = await scanner.FetchHomeSnapshotAsync(listing.ItemId, CancellationToken.None);
                        var lowestListedPrice = homeSnapshot?.LowestPrice ?? 0;
                        var suggested = lowestListedPrice > 0
                            ? Math.Max(1, lowestListedPrice - 1)
                            : listing.CurrentPrice;
                        var fillRate = scanner.GetHistoricalFillRatePercent(listing.ItemId);
                        var action = DeterminePriceAction(listing.CurrentPrice, suggested, lowestListedPrice, homeSnapshot, fillRate);
                        var trustReason = BuildInventoryTrustReason(homeSnapshot);

                        return new InventoryGridRow
                        {
                            SlotIndex = listing.SlotIndex,
                            ItemId = listing.ItemId,
                            ItemName = listing.Name,
                            CurrentSellPrice = listing.CurrentPrice,
                            UndercutPrice = suggested,
                            IsUndercut = lowestListedPrice > 0 && lowestListedPrice < listing.CurrentPrice,
                            OwnedQuantity = listing.OwnedQuantity,
                            FillRatePercent = fillRate,
                            PriceAction = action,
                            IsLowTrust = !string.IsNullOrWhiteSpace(trustReason),
                            TrustReason = trustReason
                        };
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }).ToList();

                var rows = (await Task.WhenAll(rowTasks)).ToList();

                inventoryRows = rows
                    .OrderByDescending(row => row.IsUndercut)
                    .ThenByDescending(row => row.OwnedQuantity)
                    .ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.SlotIndex)
                    .ToList();

                scanner.SaveRetainerListingSnapshots(inventoryRows.Select(row => new RetainerListingSnapshot
                {
                    SlotIndex = row.SlotIndex,
                    ItemId = row.ItemId,
                    ItemName = row.ItemName,
                    CurrentPrice = row.CurrentSellPrice,
                    SuggestedPrice = row.UndercutPrice,
                    IsUndercut = row.IsUndercut,
                    ScannedUtc = DateTime.UtcNow
                }).ToList());

                inventoryGridInitialized = true;
                inventoryStatus = inventoryRows.Count == 0
                    ? "No active retainer listings found"
                    : $"Loaded {inventoryRows.Count} retainer listings";
            }
            catch (Exception ex)
            {
                inventoryStatus = $"Inventory refresh failed: {ex.Message}";
            }
            finally
            {
                inventoryGridRefreshInProgress = false;
            }
        }

        private static string BuildRepriceKey(int slotIndex, uint itemId)
            => $"{slotIndex}:{itemId}";

        private string DeterminePriceAction(
            uint currentPrice,
            uint suggestedPrice,
            uint homeLowest,
            MarketSnapshot? homeSnapshot,
            int fillRatePercent)
        {
            if (homeLowest == 0 || homeSnapshot == null)
                return "Hold";

            var ordered = homeSnapshot.Listings
                .Where(listing => listing.PricePerUnit > 0)
                .OrderBy(listing => listing.PricePerUnit)
                .ToList();

            var spreadPercent = 0.0;
            if (ordered.Count >= 2 && ordered[0].PricePerUnit > 0)
                spreadPercent = ((ordered[1].PricePerUnit - ordered[0].PricePerUnit) / (double)ordered[0].PricePerUnit) * 100.0;

            var velocity = homeSnapshot.RecentSales.Count / (double)Math.Max(1, config.ScannerLookbackDays);

            if (currentPrice <= homeLowest)
                return "Hold";
            if (fillRatePercent < 35 && velocity < 1.0)
                return "Exit item";
            if (spreadPercent <= 0.5 && ordered.Count > 12)
                return $"Match {homeLowest:N0}";
            if (suggestedPrice < currentPrice)
                return $"Undercut {currentPrice - suggestedPrice:N0}";
            return "Hold";
        }

        private static string BuildInventoryTrustReason(MarketSnapshot? snapshot)
        {
            if (snapshot == null)
                return "No market snapshot";
            if (!snapshot.MostRecentSaleUtc.HasValue)
                return "No recent sale history";

            var ageMinutes = (DateTime.UtcNow - snapshot.MostRecentSaleUtc.Value).TotalMinutes;
            if (ageMinutes > 180)
                return "Sale history is stale";

            return string.Empty;
        }

        private void DrawSettingsTab()
        {
            var settingsChanged = false;
            ImGui.Text("Scanner Settings");
            ImGui.Separator();

            var dataCenters = WorldDataCatalog.GetDataCenters();
            var selectedDataCenter = config.DataCenterName;
            if (!dataCenters.Contains(selectedDataCenter, StringComparer.OrdinalIgnoreCase) && dataCenters.Count > 0)
            {
                selectedDataCenter = dataCenters[0];
                config.DataCenterName = selectedDataCenter;
                settingsChanged = true;
            }

            ImGui.SetNextItemWidth(260);
            if (ImGui.BeginCombo("Data Centre", selectedDataCenter))
            {
                foreach (var dataCenter in dataCenters)
                {
                    var isSelected = string.Equals(selectedDataCenter, dataCenter, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(dataCenter, isSelected))
                    {
                        selectedDataCenter = dataCenter;
                        config.DataCenterName = dataCenter;
                        settingsChanged = true;

                        var validWorlds = WorldDataCatalog.GetWorlds(dataCenter);
                        if (!WorldDataCatalog.IsWorldInDataCenter(dataCenter, config.WorldName) && validWorlds.Count > 0)
                        {
                            config.WorldName = validWorlds[0];
                            settingsChanged = true;
                        }
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Select your primary data center (Aether, Primal, Crystal, Dynamis, etc.)");

            var worlds = WorldDataCatalog.GetWorlds(config.DataCenterName);
            var selectedWorld = config.WorldName;
            if (!WorldDataCatalog.IsWorldInDataCenter(config.DataCenterName, selectedWorld) && worlds.Count > 0)
            {
                selectedWorld = worlds[0];
                config.WorldName = selectedWorld;
                settingsChanged = true;
            }

            ImGui.SetNextItemWidth(260);
            if (ImGui.BeginCombo("Home World", selectedWorld))
            {
                foreach (var world in worlds)
                {
                    var isSelected = string.Equals(selectedWorld, world, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(world, isSelected))
                    {
                        config.WorldName = world;
                        settingsChanged = true;
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Your home world where you sell items. Used for detecting your listing prices.");

            var tax = (float)config.MarketTaxRatePercent;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Tax %", ref tax, 0, 10, "%.1f%%"))
            {
                config.MarketTaxRatePercent = tax;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Market tax rate (typically 5%). Profit calculations subtract this from selling prices.");

            var velocity = (float)config.MinSaleVelocityPerDay;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Min sales/day", ref velocity, 0, 10, "%.1f"))
            {
                config.MinSaleVelocityPerDay = velocity;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Minimum items sold per day to be considered viable. Filters slow-moving items.");

            var gearVelocity = (float)config.GearMinVelocityPerDay;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Gear Min sales/day", ref gearVelocity, 0, 10, "%.1f"))
            {
                config.GearMinVelocityPerDay = gearVelocity;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Lower velocity threshold for gear-specific scans (weapons/armor/accessories) to find more opportunities.");

            var minUnitsSold24h = config.MinUnitsSold24h;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Min units sold (24h)", ref minUnitsSold24h))
            {
                config.MinUnitsSold24h = Math.Max(0, minUnitsSold24h);
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Minimum total units sold in last 24 hours. Filters unpopular items.");

            var minGil = config.MinNetProfitGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Min net profit (gil)", ref minGil))
            {
                config.MinNetProfitGil = Math.Max(0, minGil);
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Absolute minimum profit per unit (after tax). Filters low-margin flips.");

            var minPct = (float)config.MinNetProfitPercent;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Min profit %", ref minPct, 0, 100, "%.1f%%"))
            {
                config.MinNetProfitPercent = minPct;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Minimum profit as percentage of buy price. Combined with min gil filter.");

            var cheapThreshold = config.CheapItemPriceThresholdGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Cheap item threshold (gil)", ref cheapThreshold))
            {
                config.CheapItemPriceThresholdGil = Math.Max(1, cheapThreshold);
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Items at or below this price get extra scrutiny. Avoids single-unit bait listings.");

            var cheapMinQty = config.CheapItemMinProfitableQuantity;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Cheap item min profitable qty", ref cheapMinQty))
            {
                config.CheapItemMinProfitableQuantity = Math.Max(1, cheapMinQty);
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("For cheap items, requires this many units available at profitable prices. Prevents bait traps.");

            var lookback = config.ScannerLookbackDays;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Velocity lookback days", ref lookback))
            {
                config.ScannerLookbackDays = Math.Clamp(lookback, 1, 30);
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How many days of history to analyze for sales velocity.");

            var bg = config.EnableBackgroundPolling;
            if (ImGui.Checkbox("Enable background scanner polling", ref bg))
            {
                config.EnableBackgroundPolling = bg;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Periodically scan your watchlist in the background while the plugin is loaded.");

            var poll = config.PollingBaseSeconds;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Polling interval (sec)", ref poll))
            {
                config.PollingBaseSeconds = Math.Max(30, poll);
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Base interval between background scans (30+ seconds recommended).");

            var jitter = config.PollingJitterSeconds;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Polling jitter (+/- sec)", ref jitter))
            {
                config.PollingJitterSeconds = Math.Clamp(jitter, 0, 120);
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Random variance added to polling interval to avoid API rate-limit detection.");

            var autoFill = config.EnableRetainerAutoFill;
            if (ImGui.Checkbox("Enable retainer auto-fill button", ref autoFill))
            {
                config.EnableRetainerAutoFill = autoFill;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, shows an auto-fill button in the Inventory tab to quickly price items.");

            var autoTrackLiveListings = config.AutoTrackCurrentlySellingItems;
            if (ImGui.Checkbox("Auto-track items currently selling on retainers", ref autoTrackLiveListings))
            {
                config.AutoTrackCurrentlySellingItems = autoTrackLiveListings;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically includes your actively listed retainer items in watchlist scans.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Automation & Alert Quality");

            var autoBuyCapture = config.EnableAutoBuyHistoryCapture;
            if (ImGui.Checkbox("Enable auto buy-history capture", ref autoBuyCapture))
            {
                config.EnableAutoBuyHistoryCapture = autoBuyCapture;
                settingsChanged = true;
            }

            var autoConfirmBuys = config.AutoBuyHistoryAutoConfirm;
            if (ImGui.Checkbox("Auto-confirm captured buys", ref autoConfirmBuys))
            {
                config.AutoBuyHistoryAutoConfirm = autoConfirmBuys;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When disabled, buys are queued for manual confirm/edit in History tab.");

            var alertCooldown = config.UndercutAlertCooldownSeconds;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Undercut alert cooldown (sec)", ref alertCooldown))
            {
                config.UndercutAlertCooldownSeconds = Math.Max(30, alertCooldown);
                settingsChanged = true;
            }

            var alertDelta = config.UndercutAlertMinDeltaGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Undercut min delta (gil)", ref alertDelta))
            {
                config.UndercutAlertMinDeltaGil = Math.Max(1, alertDelta);
                settingsChanged = true;
            }

            var alertPctDelta = (float)config.UndercutAlertRepeatDeltaPercent;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Undercut repeat delta %", ref alertPctDelta, 0, 20, "%.1f%%"))
            {
                config.UndercutAlertRepeatDeltaPercent = Math.Max(0, alertPctDelta);
                settingsChanged = true;
            }

            ImGui.Spacing();
            ImGui.Text("World-travel planner");

            var travelOverhead = config.WorldTravelOverheadGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Travel overhead (gil)", ref travelOverhead))
            {
                config.WorldTravelOverheadGil = Math.Max(0, travelOverhead);
                settingsChanged = true;
            }

            var travelMinNet = config.WorldTravelMinNetGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Travel min net (gil)", ref travelMinNet))
            {
                config.WorldTravelMinNetGil = Math.Max(0, travelMinNet);
                settingsChanged = true;
            }

            if (ImGui.Button("Save Settings"))
            {
                config.Save();
                plugin.RefreshBackgroundPolling();
                scannerStatus = "Settings saved";
                pendingSettingsSave = false;
                lastSettingsSaveUtc = DateTime.UtcNow;
            }

            if (settingsChanged)
            {
                MarkSettingsDirty();
            }
        }

        private void DrawOpportunityTable(List<ArbitrageOpportunity> opportunities, string tableId)
        {
            ImGui.Spacing();
            ImGui.Text("Sort by Profit %:");
            ImGui.SameLine();
            if (ImGui.Button("Highest to Lowest##profitSort"))
            {
                opportunitySortDirection = SortDirection.Descending;
            }
            ImGui.SameLine();
            if (ImGui.Button("Lowest to Highest##profitSortAsc"))
            {
                opportunitySortDirection = SortDirection.Ascending;
            }
            
            var sortedOpportunities = opportunitySortDirection == SortDirection.Descending
                ? opportunities.OrderByDescending(o => o.ProfitPercent).ToList()
                : opportunities.OrderBy(o => o.ProfitPercent).ToList();

            var tableWidth = Math.Max(360f, ImGui.GetContentRegionAvail().X);
            var itemWidth = CalculateColumnWidth(
                sortedOpportunities.Select(o => o.ItemName),
                "Item",
                190f,
                Math.Max(320f, tableWidth * 1.55f));
            var buyFromWidth = CalculateColumnWidth(
                sortedOpportunities.Select(o => string.IsNullOrWhiteSpace(o.BuyFromWorld) ? config.DataCenterName : o.BuyFromWorld),
                "Buy From",
                85f,
                170f,
                80);

            if (!ImGui.BeginTable(tableId, 14,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
                new Vector2(0, 0)))
                return;

            ImGui.TableSetupColumn("Item",          ImGuiTableColumnFlags.WidthFixed, itemWidth);
            ImGui.TableSetupColumn("Owned",         ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Home Qty",      ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("Price",         ImGuiTableColumnFlags.WidthFixed, 85);
            ImGui.TableSetupColumn("Buy From",      ImGuiTableColumnFlags.WidthFixed, buyFromWidth);
            ImGui.TableSetupColumn("Buy @",         ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("Net Profit",    ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Profit %",      ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("Confidence",    ImGuiTableColumnFlags.WidthFixed, 85);
            ImGui.TableSetupColumn("Trust",         ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Data Age",      ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Sold 24h",      ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("Vel/Day",       ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Travel Plan",   ImGuiTableColumnFlags.WidthFixed, 220);
            ImGui.TableSetupColumn("Time",          ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableHeadersRow();

            foreach (var opp in sortedOpportunities)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(opp.ItemName);
                ImGui.TableNextColumn(); ImGui.Text(opp.OwnedQuantity.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.Text(opp.HomeWorldCurrentQtyListing.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.Text(opp.HomeWorldMinPrice.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.Text(string.IsNullOrWhiteSpace(opp.BuyFromWorld) ? config.DataCenterName : opp.BuyFromWorld);
                ImGui.TableNextColumn(); ImGui.Text(opp.DataCenterLowestPrice.ToString("N0"));

                ImGui.TableNextColumn();
                var netColor = opp.NetProfitPerUnit >= 0
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : new Vector4(1f, 0.4f, 0.4f, 1f);
                ImGui.TextColored(netColor, opp.NetProfitPerUnit.ToString("N0"));

                ImGui.TableNextColumn(); ImGui.Text($"{opp.ProfitPercent:F1}%");
                ImGui.TableNextColumn();
                var confidenceColor = opp.ConfidenceScore >= 70
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : opp.ConfidenceScore >= 45
                        ? new Vector4(1f, 0.85f, 0.35f, 1f)
                        : new Vector4(1f, 0.45f, 0.45f, 1f);
                ImGui.TextColored(confidenceColor, $"{opp.ConfidenceScore:F0}");
                ImGui.TableNextColumn();
                var trustColor = opp.IsLowTrust ? new Vector4(1f, 0.55f, 0.35f, 1f) : new Vector4(0.45f, 1f, 0.45f, 1f);
                ImGui.TextColored(trustColor, opp.IsLowTrust ? "Low" : "OK");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(string.IsNullOrWhiteSpace(opp.TrustReason) ? "Healthy" : opp.TrustReason);
                ImGui.TableNextColumn(); ImGui.Text($"{opp.DataFreshnessMinutes:F0}m");
                ImGui.TableNextColumn(); ImGui.Text(opp.UnitsSold24h.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.Text(opp.SaleVelocityPerDay.ToString("F2"));
                ImGui.TableNextColumn();
                var travelColor = opp.TravelWorthIt ? new Vector4(0.45f, 1f, 0.45f, 1f) : new Vector4(1f, 0.6f, 0.45f, 1f);
                ImGui.TextColored(travelColor, opp.TravelPlanSummary);
                ImGui.TableNextColumn(); ImGui.Text(opp.ScannedUtc.ToLocalTime().ToString("HH:mm:ss"));
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
            currentScanCancellation?.Dispose();
            currentScanCancellation = new CancellationTokenSource();
            
            // Set scan mode on the service
            scanner.SetScanMode(currentScanMode, null, topItemsCountUI);
            
            scannerStatus = "Scanning selected items...";
            try
            {
                var results = await scanner.ScanWatchlistAsync(currentScanCancellation.Token);
                latestResults = results.ToList();
                scannerStatus = $"Scan complete: {latestResults.Count} opportunities";
            }
            catch (OperationCanceledException)
            {
                scannerStatus = "Scan cancelled";
            }
            catch (Exception ex)
            {
                scannerStatus = $"Scan failed: {ex.Message}";
            }
            finally
            {
                scanRunning = false;
                currentScanCancellation?.Dispose();
                currentScanCancellation = null;
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

        public void Dispose()
        {
            currentScanCancellation?.Cancel();
            currentScanCancellation?.Dispose();
            currentScanCancellation = null;
        }
    }
}
