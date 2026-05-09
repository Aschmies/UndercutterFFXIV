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
        private List<CapitalAllocationPlanItem> capitalPlan = new();
        private QueueSimulationResult capitalSimulation = new();
        private List<OpportunityRejectionReason> exclusionReasons = new();
        private List<WatchedItem> cachedWatchlist = new();
        private List<WatchlistSuggestion> cachedSuggestions = new();
        private List<InventoryGridRow> inventoryRows = new();
        private readonly List<InventoryGridRow> repriceQueue = new();
        private readonly HashSet<string> repriceQueueKeys = new(StringComparer.Ordinal);
        private bool scanRunning;
        private CancellationTokenSource? currentScanCancellation;
        private string scannerStatus = "Idle";
        private bool opportunityCompactMode;
        private bool opportunityDetailedTooltips = true;
        private int opportunityRowLimit = 120;
        private bool inventoryGridRefreshInProgress;
        private bool inventoryGridInitialized;
        private bool exclusionRefreshInProgress;
        private DateTime lastExclusionRefreshUtc = DateTime.MinValue;
        private DateTime lastExclusionRequestUtc = DateTime.MinValue;
        
        // Full-market scan mode
        private ScanMode currentScanMode = ScanMode.TopItems;
        private int topItemsCountUI = 250;
        
        // Copy feedback
        private DateTime lastCopyTime = DateTime.MinValue;
        private DateTime lastAutoFillTime = DateTime.MinValue;
        private DateTime lastSettingsSaveUtc = DateTime.MinValue;
        private bool pendingSettingsSave;
        private string inventoryStatus = string.Empty;

        // Auto-queue run state machine
        private enum AutoQueueState { Idle, ClickingItem, Filling, Confirming }
        private AutoQueueState autoQueueState = AutoQueueState.Idle;
        private DateTime autoQueueLastAction = DateTime.MinValue;
        private bool autoQueueRunning;
        private string historyStatus = string.Empty;
        private string dashboardStatus = string.Empty;
        private string exclusionStatus = string.Empty;
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
            public double EstimatedNetPerUnit { get; init; }
            public bool GuardrailPass { get; init; }
            public string GuardrailReason { get; init; } = string.Empty;
            public double ListedAgeHours { get; init; }
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
            cachedSuggestions = scanner.GetWatchlistSuggestions(8).ToList();
            latestResults = scanner.GetLastResults().ToList();
            RefreshCapitalPlan();
            _ = RefreshExclusionInspectorAsync(force: false);
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
            var feedback = scanner.GetRecommendationFeedbackSummary(30);
            ImGui.TextDisabled($"Recommendation acceptance (30d): {feedback.AcceptanceRatePercent:F1}% ({feedback.AcceptedCount}/{feedback.AcceptedCount + feedback.RejectedCount})");
            if (ImGui.Button("Export Session Report"))
            {
                try
                {
                    var reportPath = scanner.ExportSessionReport(
                        MarketAssistantPlugin.PluginInterface.GetPluginConfigDirectory(),
                        latestResults,
                        repriceQueue.Count,
                        inventoryRows.Count);
                    dashboardStatus = $"Exported report: {reportPath}";
                }
                catch (Exception ex)
                {
                    dashboardStatus = $"Report export failed: {ex.Message}";
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Writes a plain-text session summary with top opportunities, feedback stats, and queue status.");
            if (!string.IsNullOrWhiteSpace(dashboardStatus))
                ImGui.TextDisabled(dashboardStatus);

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
            ImGui.Text("Scan Profile:");
            ImGui.SameLine();
            if (ImGui.SmallButton("Balanced##profile"))
            {
                scanner.ApplyScanProfile("balanced");
                config.Save();
                scannerStatus = "Applied Balanced profile";
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Quick##profile"))
            {
                scanner.ApplyScanProfile("quick");
                config.Save();
                scannerStatus = "Applied Quick profile";
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Conservative##profile"))
            {
                scanner.ApplyScanProfile("conservative");
                config.Save();
                scannerStatus = "Applied Conservative profile";
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("High Volume##profile"))
            {
                scanner.ApplyScanProfile("high-volume");
                config.Save();
                scannerStatus = "Applied High Volume profile";
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"Active: {config.ActiveScanProfile}");

            var health = scanner.GetApiHealth();
            if (config.EnableDegradedModeActionBlock && health.SafeZone != "Green")
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.35f, 1f), "Degraded mode: risky actions are blocked until API health recovers.");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("When enabled, low-trust opportunities and aggressive repricing actions are blocked in Yellow/Red API conditions.");
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

            ImGui.Spacing();
            ImGui.Text("Suggested Watchlist Adds");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Auto-discovery suggestions based on your profitable trade patterns over the last 30 days.");
            ImGui.Separator();
            if (cachedSuggestions.Count == 0)
                cachedSuggestions = scanner.GetWatchlistSuggestions(8).ToList();

            foreach (var suggestion in cachedSuggestions.Take(8).ToList())
            {
                ImGui.TextUnformatted(suggestion.ItemName);
                ImGui.SameLine();
                ImGui.TextDisabled($"({suggestion.Reason})");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Watch##suggest{suggestion.ItemId}"))
                {
                    scanner.AddWatchItem(new ItemLookup { ItemId = suggestion.ItemId, Name = suggestion.ItemName });
                    RefreshWatchlist();
                    cachedSuggestions = scanner.GetWatchlistSuggestions(8).ToList();
                }
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
            {
                RefreshCapitalPlan();

                ImGui.Text("Capital Allocator");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Distributes daily capital across top opportunities using confidence, velocity, trust, and profit quality.");
                ImGui.TextDisabled($"Budget used: {capitalSimulation.TotalCostGil:N0} / {config.MaxCapitalPerDayGil:N0} | Allocated items: {capitalSimulation.ItemCount}");

                if (ImGui.BeginTable("##capitalPlan", 8,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                    new Vector2(0, 140)))
                {
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Buy From", ImGuiTableColumnFlags.WidthFixed, 75);
                    ImGui.TableSetupColumn("Buy @", ImGuiTableColumnFlags.WidthFixed, 75);
                    ImGui.TableSetupColumn("Sell @", ImGuiTableColumnFlags.WidthFixed, 75);
                    ImGui.TableSetupColumn("Profit %", ImGuiTableColumnFlags.WidthFixed, 65);
                    ImGui.TableSetupColumn("Priority", ImGuiTableColumnFlags.WidthFixed, 62);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40);
                    ImGui.TableSetupColumn("Est. Return", ImGuiTableColumnFlags.WidthFixed, 85);
                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                    ImGui.TableNextColumn(); ImGui.TableHeader("Item");
                    ImGui.TableNextColumn(); ImGui.TableHeader("Buy From");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("The world with the lowest listed price for this item — travel here to buy.");
                    ImGui.TableNextColumn();
                    ImGui.TableHeader("Buy @");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Cheapest listed price on the source world. This is what you pay per unit.");
                    ImGui.TableNextColumn();
                    ImGui.TableHeader("Sell @");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Current lowest listing on your home world.\nList at this price or 1 gil below to be the cheapest seller.");
                    ImGui.TableNextColumn(); ImGui.TableHeader("Profit %");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("(Sell @ − Buy @) / Buy @ — gross margin before taxes/fees.");
                    ImGui.TableNextColumn();
                    ImGui.TableHeader("Priority");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Capital allocation priority — higher = allocated first.\n\nCombines Confidence with velocity, profit margin, trust, and market regime:\n  Confidence x velocity factor (0.4-1.3x) x profit factor (0.35-1.4x)\n  x trust (1.0 OK / 0.55 Low Trust) x regime penalty.\n\nUnlike Confidence which only measures data reliability,\nPriority also weights how liquid and profitable the market is.");
                    ImGui.TableNextColumn(); ImGui.TableHeader("Qty");
                    ImGui.TableNextColumn();
                    ImGui.TableHeader("Est. Return");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Estimated total profit for this allocation.\n\nCalculation: (Sell @ − Buy @) × Qty\n  Buy from source world at Buy @\n  Sell on your home world at Sell @\n\nHover each row for the full per-item breakdown.");

                    foreach (var allocation in capitalPlan.Take(14))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(allocation.ItemName);
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(string.IsNullOrWhiteSpace(allocation.BuyFromWorld) ? config.DataCenterName : allocation.BuyFromWorld);
                        ImGui.TableNextColumn(); ImGui.Text(allocation.UnitBuyPrice.ToString("N0"));
                        ImGui.TableNextColumn(); ImGui.Text(allocation.SuggestedSellPrice.ToString("N0"));
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Current home-world floor price. List at or just below this to undercut existing sellers.");
                        ImGui.TableNextColumn(); ImGui.Text($"{allocation.ProfitPercent:F1}%");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{allocation.Score:F0}");
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip($"Allocation priority: {allocation.Score:F0}\nFormula: Confidence x velocity factor x profit factor x trust x regime penalties.\nSee the Confidence column in Flip Opportunities for the data quality breakdown.");
                        ImGui.TableNextColumn(); ImGui.Text(allocation.AllocatedQty.ToString("N0"));
                        ImGui.TableNextColumn();
                        var netColor = allocation.ProjectedNetGil >= 0
                            ? new Vector4(0.4f, 1f, 0.4f, 1f)
                            : new Vector4(1f, 0.45f, 0.45f, 1f);
                        ImGui.TextColored(netColor, allocation.ProjectedNetGil.ToString("N0"));
                        if (ImGui.IsItemHovered())
                        {
                            var totalCost = allocation.UnitBuyPrice * allocation.AllocatedQty;
                            var totalRevenue = (double)allocation.SuggestedSellPrice * allocation.AllocatedQty;
                            var buyWorld = string.IsNullOrWhiteSpace(allocation.BuyFromWorld) ? config.DataCenterName : allocation.BuyFromWorld;
                            ImGui.SetTooltip(
                                $"Buy: {allocation.AllocatedQty} × {allocation.UnitBuyPrice:N0} gil on {buyWorld} = {totalCost:N0} gil\n" +
                                $"Sell: {allocation.AllocatedQty} × {allocation.SuggestedSellPrice:N0} gil on {config.WorldName} = {totalRevenue:N0} gil\n" +
                                $"\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\n" +
                                $"Est. Return: {allocation.ProjectedNetGil:N0} gil ({allocation.ProfitPercent:F1}% margin)");
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.Text("Queue Simulator");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Pre-flight outcome estimate for the allocated execution queue with best/base/worst scenarios.");
                ImGui.TextDisabled($"Best {capitalSimulation.BestCaseNetGil:N0} | Base {capitalSimulation.BaseCaseNetGil:N0} | Worst {capitalSimulation.WorstCaseNetGil:N0}");
                ImGui.TextDisabled($"Expected value {capitalSimulation.ExpectedValueNetGil:N0} | Estimated liquidity delay {capitalSimulation.EstimatedLiquidityDelayHours:F1}h");

                ImGui.Spacing();
                var plans = scanner.GetTravelBatchPlans(latestResults, 3);
                if (plans.Count > 0)
                {
                    ImGui.Text("Travel Batch Planner");
                    foreach (var plan in plans)
                        ImGui.BulletText($"{config.WorldName} -> {plan.World} -> {config.WorldName}: est net {plan.ProjectedNetGil:N0} across {plan.ItemCount} items");
                    ImGui.Spacing();
                }

                ImGui.Text("Why Not Included?");
                ImGui.SameLine();
                if (ImGui.Button("Refresh Inspector##exclusions") && !exclusionRefreshInProgress)
                    _ = RefreshExclusionInspectorAsync(force: true);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Explains why watchlist items were filtered from current opportunities.");
                ImGui.TextDisabled(exclusionStatus);
                if (exclusionReasons.Count > 0)
                {
                    if (ImGui.BeginTable("##exclusionTable", 2,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                        new Vector2(0, 120)))
                    {
                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableHeadersRow();

                        foreach (var exclusion in exclusionReasons.Take(20))
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn(); ImGui.TextUnformatted(exclusion.ItemName);
                            ImGui.TableNextColumn(); ImGui.TextUnformatted(exclusion.Reason);
                        }

                        ImGui.EndTable();
                    }
                }

                ImGui.Spacing();
                DrawOpportunityTable(latestResults, "##scannerOppTable");
            }
        }

        private void DrawInventoryTab()
        {
            if (!inventoryGridInitialized && !inventoryGridRefreshInProgress)
                _ = RefreshInventoryGridAsync();

            ImGui.Text("Retainer Listings");
            ImGui.TextDisabled("Live rows for items currently selling. Suggested price is based on Home World lowest listed price.");

            var retainerWindowDetected = config.EnableRetainerAutoFill && retainerPriceService.IsRetainerSellWindowOpen();
            var retainerListDetected   = config.EnableRetainerAutoFill && retainerPriceService.IsRetainerSellListOpen();
            var retainerAnyDetected    = retainerWindowDetected || retainerListDetected;

            // ── Auto-queue state machine ──────────────────────────────────────────────
            if (autoQueueRunning && config.EnableRetainerAutoFill)
            {
                var elapsed = (DateTime.Now - autoQueueLastAction).TotalMilliseconds;
                switch (autoQueueState)
                {
                    case AutoQueueState.Idle:
                        if (repriceQueue.Count > 0 && retainerListDetected)
                        {
                            var next = repriceQueue[0];
                            retainerPriceService.TryClickRetainerSellListItem(next.SlotIndex, out _);
                            inventoryStatus = $"Auto: opening Adjust Price for {next.ItemName}…";
                            autoQueueState = AutoQueueState.ClickingItem;
                            autoQueueLastAction = DateTime.Now;
                        }
                        else if (repriceQueue.Count == 0)
                        {
                            autoQueueRunning = false;
                            inventoryStatus = "Auto-queue complete.";
                        }
                        break;

                    case AutoQueueState.ClickingItem:
                        // Wait up to 1.5 s for the Adjust Price dialog to appear
                        if (retainerWindowDetected)
                        {
                            autoQueueState = AutoQueueState.Filling;
                            autoQueueLastAction = DateTime.Now;
                        }
                        else if (elapsed > 1500)
                        {
                            // Didn't open — skip this item and try next
                            inventoryStatus = $"Auto: could not open Adjust Price, skipping {repriceQueue[0].ItemName}";
                            repriceQueue.RemoveAt(0);
                            autoQueueState = AutoQueueState.Idle;
                            autoQueueLastAction = DateTime.Now;
                        }
                        break;

                    case AutoQueueState.Filling:
                        if (elapsed > 150) // small delay so the dialog finishes rendering
                        {
                            var next = repriceQueue[0];
                            retainerPriceService.TryAutoFillPrice(next.UndercutPrice, out var fillStatus);
                            lastAutoFillTime = DateTime.Now;
                            inventoryStatus = $"Auto: filled {next.ItemName} → {next.UndercutPrice:N0} gil";
                            autoQueueState = AutoQueueState.Confirming;
                            autoQueueLastAction = DateTime.Now;
                        }
                        break;

                    case AutoQueueState.Confirming:
                        if (elapsed > 250) // small delay after fill before confirming
                        {
                            retainerPriceService.TryConfirmAdjustPrice(out _);
                            var next = repriceQueue[0];
                            var key = BuildRepriceKey(next.SlotIndex, next.ItemId);
                            repriceQueue.RemoveAt(0);
                            repriceQueueKeys.Remove(key);
                            inventoryStatus = $"Auto: confirmed {next.ItemName}. {repriceQueue.Count} left.";
                            autoQueueState = AutoQueueState.Idle;
                            autoQueueLastAction = DateTime.Now;
                        }
                        break;
                }
            }
            // ─────────────────────────────────────────────────────────────────────────

            if (config.EnableRetainerAutoFill)
            {
                var detectionColor = retainerAnyDetected
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : new Vector4(0.95f, 0.75f, 0.25f, 1f);
                var detectionText = retainerWindowDetected
                    ? "Retainer window detected"
                    : retainerListDetected
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
            ImGui.BeginDisabled(repriceQueue.Count == 0 || !(config.EnableRetainerAutoFill && retainerAnyDetected));
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

            // ── Auto-run queue button ─────────────────────────────────────────────────
            if (autoQueueRunning)
            {
                if (ImGui.Button("Stop Auto"))
                {
                    autoQueueRunning = false;
                    autoQueueState = AutoQueueState.Idle;
                    inventoryStatus = "Auto-queue stopped.";
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Stops the automatic queue processing.");
            }
            else
            {
                ImGui.BeginDisabled(repriceQueue.Count == 0 || !(config.EnableRetainerAutoFill && retainerListDetected));
                if (ImGui.Button("Run Queue Auto"))
                {
                    autoQueueRunning = true;
                    autoQueueState = AutoQueueState.Idle;
                    autoQueueLastAction = DateTime.MinValue;
                    inventoryStatus = "Auto-queue started…";
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically opens each queued item's Adjust Price dialog, fills the suggested price, and confirms — one by one.\nRequires the retainer Markets window to be open.");
                ImGui.EndDisabled();
            }
            ImGui.SameLine();
            // ─────────────────────────────────────────────────────────────────────────
            ImGui.BeginDisabled(repriceQueue.Count == 0);
            if (ImGui.Button("Clear Queue"))
            {
                repriceQueue.Clear();
                repriceQueueKeys.Clear();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Queue All Safe Items"))
            {
                foreach (var row in inventoryRows)
                {
                    if (!row.GuardrailPass || row.UndercutPrice == 0)
                        continue;

                    if (config.EnableDegradedModeActionBlock && row.IsLowTrust)
                        continue;

                    var key = BuildRepriceKey(row.SlotIndex, row.ItemId);
                    if (repriceQueueKeys.Contains(key))
                        continue;

                    repriceQueue.Add(row);
                    repriceQueueKeys.Add(key);
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Queues only rows that pass repricing guardrails and trust checks.");

            var queueProjected = repriceQueue.Sum(row => row.EstimatedNetPerUnit);
            ImGui.TextDisabled($"Queue projected net/unit total: {queueProjected:N0} gil");

            ImGui.Spacing();
            if (inventoryRows.Count == 0)
            {
                ImGui.TextDisabled("No active retainer market listings detected. Open your retainer market inventory and refresh.");
            }
            else if (ImGui.BeginTable("##inventoryGrid", 10,
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
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Fill rate: {row.FillRatePercent}% | Listed age: {row.ListedAgeHours:F1}h");

                    ImGui.TableNextColumn();
                    var trustText = row.IsLowTrust ? "Low trust" : "Healthy";
                    var trustColor = row.IsLowTrust
                        ? new Vector4(0.95f, 0.55f, 0.35f, 1f)
                        : new Vector4(0.4f, 1f, 0.4f, 1f);
                    ImGui.TextColored(trustColor, trustText);
                    if (ImGui.IsItemHovered())
                    {
                        var trustTooltip = string.IsNullOrWhiteSpace(row.TrustReason) ? "Healthy" : row.TrustReason;
                        if (!row.GuardrailPass)
                            trustTooltip = $"{trustTooltip}\nGuardrail: {row.GuardrailReason}";
                        ImGui.SetTooltip(trustTooltip);
                    }

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
                    ImGui.BeginDisabled(!(config.EnableRetainerAutoFill && retainerWindowDetected && row.UndercutPrice > 0 && row.GuardrailPass && !(config.EnableDegradedModeActionBlock && row.IsLowTrust)));
                    if (ImGui.SmallButton($"Fill##fill{row.SlotIndex}_{row.ItemId}"))
                    {
                        if (retainerPriceService.TryAutoFillPrice(row.UndercutPrice, out var status))
                            lastAutoFillTime = DateTime.Now;
                        inventoryStatus = status;
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Clicks Adjust Price first, then fills the retainer sell-price input. Blocked automatically if guardrails or trust checks fail.");
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
                        ImGui.BeginDisabled(row.UndercutPrice == 0 || !row.GuardrailPass || (config.EnableDegradedModeActionBlock && row.IsLowTrust));
                        if (ImGui.SmallButton($"Queue##queue{row.SlotIndex}_{row.ItemId}"))
                        {
                            repriceQueue.Add(row);
                            repriceQueueKeys.Add(queueKey);
                        }
                        if (ImGui.IsItemHovered() && !row.GuardrailPass)
                            ImGui.SetTooltip(row.GuardrailReason);
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
            if (config.EnableRetainerAutoFill && !retainerAnyDetected)
                ImGui.TextDisabled("Open your retainer's Markets window to enable auto-fill and Run Queue Auto.");
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
                var historySnapshots = scanner.GetRetainerListingSnapshots(30);
                var now = DateTime.UtcNow;
                var ageByKey = historySnapshots
                    .GroupBy(snapshot => BuildRepriceKey(snapshot.SlotIndex, snapshot.ItemId))
                    .ToDictionary(
                        group => group.Key,
                        group => Math.Max(0, (now - group.Min(snapshot => snapshot.ScannedUtc)).TotalHours));

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
                        var avgBuy = scanner.GetAverageBuyPrice(listing.ItemId, 60);
                        var estimatedNetPerUnit = avgBuy.HasValue
                            ? (suggested * (1 - (config.MarketTaxRatePercent / 100.0))) - avgBuy.Value
                            : 0;
                        var guardrail = EvaluateRepriceGuardrail(listing.CurrentPrice, suggested, estimatedNetPerUnit, avgBuy);
                        var key = BuildRepriceKey(listing.SlotIndex, listing.ItemId);
                        var ageHours = ageByKey.TryGetValue(key, out var age) ? age : 0;

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
                            TrustReason = trustReason,
                            EstimatedNetPerUnit = estimatedNetPerUnit,
                            GuardrailPass = guardrail.Pass,
                            GuardrailReason = guardrail.Reason,
                            ListedAgeHours = ageHours
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
            if (velocity < 0.7)
                return "Aging listing";
            if (suggestedPrice < currentPrice)
                return $"Undercut {currentPrice - suggestedPrice:N0}";
            return "Hold";
        }

        private (bool Pass, string Reason) EvaluateRepriceGuardrail(uint currentPrice, uint suggestedPrice, double estimatedNetPerUnit, double? avgBuyPrice)
        {
            if (!config.EnableStrictRepriceGuardrails)
                return (true, string.Empty);

            if (suggestedPrice == 0)
                return (false, "Suggested price is unavailable.");

            if (!avgBuyPrice.HasValue)
                return (true, "No recorded buy history for this item.");

            var marginPercent = avgBuyPrice.Value <= 0 ? 0 : (estimatedNetPerUnit / avgBuyPrice.Value) * 100.0;
            if (estimatedNetPerUnit < config.MinRepriceMarginGil)
                return (false, $"Projected net ({estimatedNetPerUnit:N0}) is below minimum gil margin ({config.MinRepriceMarginGil:N0}).");
            if (marginPercent < config.MinRepriceMarginPercent)
                return (false, $"Projected margin ({marginPercent:F1}%) is below minimum margin ({config.MinRepriceMarginPercent:F1}%).");
            if (suggestedPrice > currentPrice * 2)
                return (false, "Suggested price appears anomalous versus current listing.");

            return (true, "Pass");
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

            ImGui.Spacing();
            ImGui.Text("Guardrails & Position Sizing");

            var strictGuardrails = config.EnableStrictRepriceGuardrails;
            if (ImGui.Checkbox("Enable strict repricing guardrails", ref strictGuardrails))
            {
                config.EnableStrictRepriceGuardrails = strictGuardrails;
                settingsChanged = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Blocks queue/fill actions that fail your minimum margin thresholds.");

            var degradedBlock = config.EnableDegradedModeActionBlock;
            if (ImGui.Checkbox("Block risky actions in degraded mode", ref degradedBlock))
            {
                config.EnableDegradedModeActionBlock = degradedBlock;
                settingsChanged = true;
            }

            var minMarginGil = config.MinRepriceMarginGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Min reprice margin (gil)", ref minMarginGil))
            {
                config.MinRepriceMarginGil = Math.Max(0, minMarginGil);
                settingsChanged = true;
            }

            var minMarginPct = (float)config.MinRepriceMarginPercent;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Min reprice margin %", ref minMarginPct, 0, 30, "%.1f%%"))
            {
                config.MinRepriceMarginPercent = Math.Max(0, minMarginPct);
                settingsChanged = true;
            }

            var capPerItem = config.MaxCapitalPerItemGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Max capital per item (gil)", ref capPerItem))
            {
                config.MaxCapitalPerItemGil = Math.Max(1000, capPerItem);
                settingsChanged = true;
            }

            var capPerDay = config.MaxCapitalPerDayGil;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputInt("Max capital per day (gil)", ref capPerDay))
            {
                config.MaxCapitalPerDayGil = Math.Max(1000, capPerDay);
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

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("Rows##oppRowLimit", ref opportunityRowLimit))
                opportunityRowLimit = Math.Clamp(opportunityRowLimit, 20, 500);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Maximum rows rendered in the opportunities table.");

            ImGui.SameLine();
            ImGui.Checkbox("Compact##oppCompact", ref opportunityCompactMode);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show a lighter, faster table with essential fields only.");

            ImGui.SameLine();
            ImGui.Checkbox("Detailed tooltips##oppTooltips", ref opportunityDetailedTooltips);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When disabled, tooltip text generation is minimized for smoother scrolling.");
            
            var sortedOpportunities = opportunitySortDirection == SortDirection.Descending
                ? opportunities.OrderByDescending(o => o.ProfitPercent).ToList()
                : opportunities.OrderBy(o => o.ProfitPercent).ToList();
            var displayedOpportunities = sortedOpportunities.Take(opportunityRowLimit).ToList();

            if (sortedOpportunities.Count > displayedOpportunities.Count)
                ImGui.TextDisabled($"Showing {displayedOpportunities.Count:N0} / {sortedOpportunities.Count:N0} rows");

            var tableWidth = Math.Max(360f, ImGui.GetContentRegionAvail().X);
            var itemWidth = CalculateColumnWidth(
                displayedOpportunities.Select(o => o.ItemName),
                "Item",
                190f,
                Math.Max(320f, tableWidth * 1.55f));
            var buyFromWidth = CalculateColumnWidth(
                displayedOpportunities.Select(o => string.IsNullOrWhiteSpace(o.BuyFromWorld) ? config.DataCenterName : o.BuyFromWorld),
                "Buy From",
                85f,
                170f,
                80);

            if (opportunityCompactMode)
            {
                if (!ImGui.BeginTable(tableId, 12,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
                    new Vector2(0, 0)))
                    return;

                ImGui.TableSetupColumn("Item",       ImGuiTableColumnFlags.WidthFixed, itemWidth);
                ImGui.TableSetupColumn("Buy From",   ImGuiTableColumnFlags.WidthFixed, buyFromWidth);
                ImGui.TableSetupColumn("Buy @",      ImGuiTableColumnFlags.WidthFixed, 75);
                ImGui.TableSetupColumn("Net Profit", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Profit %",   ImGuiTableColumnFlags.WidthFixed, 75);
                ImGui.TableSetupColumn("Confidence", ImGuiTableColumnFlags.WidthFixed, 85);
                ImGui.TableSetupColumn("Trust",      ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Data Age",   ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Vel/Day",    ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Rec Qty",    ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Batch Net",  ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Time",       ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn(); ImGui.TableHeader("Item");
                ImGui.TableNextColumn(); ImGui.TableHeader("Buy From");
                ImGui.TableNextColumn(); ImGui.TableHeader("Buy @");
                ImGui.TableNextColumn(); ImGui.TableHeader("Net Profit");
                ImGui.TableNextColumn(); ImGui.TableHeader("Profit %");
                ImGui.TableNextColumn();
                ImGui.TableHeader("Confidence");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Data reliability score (0-100).\n\n  Velocity     0-40 pts  how fast the item sells per day\n  Spread       0-20 pts  how tight the gap between cheapest listings is\n  Depth        0-15 pts  how distributed supply is (not one whale)\n  Volatility   0-15 pts  how stable the price history is\n  Freshness    0-10 pts  how recent the market data is\n  Profit bonus 0-10 pts  extra weight for high-margin items\n  API penalty  0/-5/-15  deducted for Universalis health issues\n\nGreen >= 70  |  Yellow >= 45  |  Red < 45\n\nMeasures data quality only. The Priority column (Capital Allocator)\nalso weights velocity, profit, trust, and market regime.");
                ImGui.TableNextColumn(); ImGui.TableHeader("Trust");
                ImGui.TableNextColumn(); ImGui.TableHeader("Data Age");
                ImGui.TableNextColumn(); ImGui.TableHeader("Vel/Day");
                ImGui.TableNextColumn(); ImGui.TableHeader("Rec Qty");
                ImGui.TableNextColumn(); ImGui.TableHeader("Batch Net");
                ImGui.TableNextColumn(); ImGui.TableHeader("Time");

                foreach (var opp in displayedOpportunities)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(opp.ItemName);
                    if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{opp.ExplainabilitySummary}");
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
                    if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Data reliability: {opp.ConfidenceScore:F0} / 100\n\n  Velocity     {opp.ScoreVelocity:F1} / 40  - sales per day\n  Spread       {opp.ScoreSpread:F1} / 20  - price gap tightness between cheapest listings\n  Depth        {opp.ScoreDepth:F1} / 15  - supply not concentrated in one seller\n  Volatility   {opp.ScoreVolatility:F1} / 15  - price stability\n  Freshness    {opp.ScoreFreshness:F1} / 10  - data recency\n  API penalty -{opp.ScoreApiPenalty:F1}       - Universalis health deduction");

                    ImGui.TableNextColumn();
                    var trustColor = opp.IsLowTrust ? new Vector4(1f, 0.55f, 0.35f, 1f) : new Vector4(0.45f, 1f, 0.45f, 1f);
                    ImGui.TextColored(trustColor, opp.IsLowTrust ? "Low" : "OK");
                    if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{(string.IsNullOrWhiteSpace(opp.TrustReason) ? "Healthy" : opp.TrustReason)}\nManual review: {(opp.NeedsManualReview ? "Required" : "No")}");

                    ImGui.TableNextColumn(); ImGui.Text($"{opp.DataFreshnessMinutes:F0}m");
                    ImGui.TableNextColumn(); ImGui.Text(opp.SaleVelocityPerDay.ToString("F2"));
                    ImGui.TableNextColumn(); ImGui.Text(opp.RecommendedBuyQty.ToString("N0"));
                    ImGui.TableNextColumn(); ImGui.Text(opp.ProjectedBatchNetGil.ToString("N0"));
                    ImGui.TableNextColumn(); ImGui.Text(opp.ScannedUtc.ToLocalTime().ToString("HH:mm:ss"));
                }

                ImGui.EndTable();
                return;
            }

            if (!ImGui.BeginTable(tableId, 20,
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
            ImGui.TableSetupColumn("Regime",        ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Sold 24h",      ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("Vel/Day",       ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Rec Qty",       ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Batch Net",     ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Travel Plan",   ImGuiTableColumnFlags.WidthFixed, 220);
            ImGui.TableSetupColumn("Route",         ImGuiTableColumnFlags.WidthFixed, 170);
            ImGui.TableSetupColumn("Feedback",      ImGuiTableColumnFlags.WidthFixed, 95);
            ImGui.TableSetupColumn("Time",          ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableNextColumn(); ImGui.TableHeader("Item");
            ImGui.TableNextColumn(); ImGui.TableHeader("Owned");
            ImGui.TableNextColumn(); ImGui.TableHeader("Home Qty");
            ImGui.TableNextColumn(); ImGui.TableHeader("Price");
            ImGui.TableNextColumn(); ImGui.TableHeader("Buy From");
            ImGui.TableNextColumn(); ImGui.TableHeader("Buy @");
            ImGui.TableNextColumn(); ImGui.TableHeader("Net Profit");
            ImGui.TableNextColumn(); ImGui.TableHeader("Profit %");
            ImGui.TableNextColumn();
            ImGui.TableHeader("Confidence");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Data reliability score (0-100).\n\n  Velocity     0-40 pts  how fast the item sells per day\n  Spread       0-20 pts  how tight the gap between cheapest listings is\n  Depth        0-15 pts  how distributed supply is (not one whale)\n  Volatility   0-15 pts  how stable the price history is\n  Freshness    0-10 pts  how recent the market data is\n  Profit bonus 0-10 pts  extra weight for high-margin items\n  API penalty  0/-5/-15  deducted for Universalis health issues\n\nGreen >= 70  |  Yellow >= 45  |  Red < 45\n\nMeasures data quality only. The Priority column (Capital Allocator)\nalso weights velocity, profit, trust, and market regime.");
            ImGui.TableNextColumn(); ImGui.TableHeader("Trust");
            ImGui.TableNextColumn(); ImGui.TableHeader("Data Age");
            ImGui.TableNextColumn(); ImGui.TableHeader("Regime");
            ImGui.TableNextColumn(); ImGui.TableHeader("Sold 24h");
            ImGui.TableNextColumn(); ImGui.TableHeader("Vel/Day");
            ImGui.TableNextColumn(); ImGui.TableHeader("Rec Qty");
            ImGui.TableNextColumn(); ImGui.TableHeader("Batch Net");
            ImGui.TableNextColumn(); ImGui.TableHeader("Travel Plan");
            ImGui.TableNextColumn(); ImGui.TableHeader("Route");
            ImGui.TableNextColumn(); ImGui.TableHeader("Feedback");
            ImGui.TableNextColumn(); ImGui.TableHeader("Time");

            foreach (var opp in displayedOpportunities)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(opp.ItemName);
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{opp.ExplainabilitySummary}");
                ImGui.TableNextColumn(); ImGui.Text(opp.OwnedQuantity.ToString("N0"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Owned quantity in inventory + active retainer listings. Used to prioritize opportunities you can execute immediately.");
                ImGui.TableNextColumn(); ImGui.Text(opp.HomeWorldCurrentQtyListing.ToString("N0"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Current quantity listed at the home-world minimum price.");
                ImGui.TableNextColumn(); ImGui.Text(opp.HomeWorldMinPrice.ToString("N0"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Current home-world floor listing price used for projected sell-side revenue.");
                ImGui.TableNextColumn(); ImGui.Text(string.IsNullOrWhiteSpace(opp.BuyFromWorld) ? config.DataCenterName : opp.BuyFromWorld);
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Best source world for buy-side execution.");
                ImGui.TableNextColumn(); ImGui.Text(opp.DataCenterLowestPrice.ToString("N0"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Estimated buy price from source world listings.");

                ImGui.TableNextColumn();
                var netColor = opp.NetProfitPerUnit >= 0
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : new Vector4(1f, 0.4f, 0.4f, 1f);
                ImGui.TextColored(netColor, opp.NetProfitPerUnit.ToString("N0"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Projected net per unit after tax using home sell floor and buy source floor.");

                ImGui.TableNextColumn(); ImGui.Text($"{opp.ProfitPercent:F1}%");
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Projected return percentage versus estimated buy price.");
                ImGui.TableNextColumn();
                var confidenceColor = opp.ConfidenceScore >= 70
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : opp.ConfidenceScore >= 45
                        ? new Vector4(1f, 0.85f, 0.35f, 1f)
                        : new Vector4(1f, 0.45f, 0.45f, 1f);
                ImGui.TextColored(confidenceColor, $"{opp.ConfidenceScore:F0}");
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Data reliability: {opp.ConfidenceScore:F0} / 100\n\n  Velocity     {opp.ScoreVelocity:F1} / 40  - sales per day\n  Spread       {opp.ScoreSpread:F1} / 20  - price gap tightness between cheapest listings\n  Depth        {opp.ScoreDepth:F1} / 15  - supply not concentrated in one seller\n  Volatility   {opp.ScoreVolatility:F1} / 15  - price stability\n  Freshness    {opp.ScoreFreshness:F1} / 10  - data recency\n  API penalty -{opp.ScoreApiPenalty:F1}       - Universalis health deduction");
                ImGui.TableNextColumn();
                var trustColor = opp.IsLowTrust ? new Vector4(1f, 0.55f, 0.35f, 1f) : new Vector4(0.45f, 1f, 0.45f, 1f);
                ImGui.TextColored(trustColor, opp.IsLowTrust ? "Low" : "OK");
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{(string.IsNullOrWhiteSpace(opp.TrustReason) ? "Healthy" : opp.TrustReason)}\nManual review: {(opp.NeedsManualReview ? "Required" : "No")}");
                ImGui.TableNextColumn(); ImGui.Text($"{opp.DataFreshnessMinutes:F0}m");
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Minutes since the most recent observed sale in source data.");
                ImGui.TableNextColumn(); ImGui.Text(opp.RiskRegime);
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Detected market regime used to contextualize confidence and sizing.");
                ImGui.TableNextColumn(); ImGui.Text(opp.UnitsSold24h.ToString("N0"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Units sold in the last 24 hours.");
                ImGui.TableNextColumn(); ImGui.Text(opp.SaleVelocityPerDay.ToString("F2"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Average sales/day over configured lookback window.");
                ImGui.TableNextColumn(); ImGui.Text(opp.RecommendedBuyQty.ToString("N0"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Position sizing recommendation capped by confidence, velocity, and item capital limit ({opp.MaxAffordableQtyByCapital}).");
                ImGui.TableNextColumn(); ImGui.Text(opp.ProjectedBatchNetGil.ToString("N0"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Projected net if buying recommended quantity.");
                ImGui.TableNextColumn();
                var travelColor = opp.TravelWorthIt ? new Vector4(0.45f, 1f, 0.45f, 1f) : new Vector4(1f, 0.6f, 0.45f, 1f);
                ImGui.TextColored(travelColor, opp.TravelPlanSummary);
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Travel planner includes overhead and minimum net threshold settings.");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(opp.RouteSummary);
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Suggested world route for this opportunity.");
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"+##fbAccept{opp.ItemId}"))
                    scanner.RecordRecommendationFeedback(opp, true);
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Mark as accepted to train your personalized feedback loop.");
                ImGui.SameLine();
                if (ImGui.SmallButton($"-##fbReject{opp.ItemId}"))
                    scanner.RecordRecommendationFeedback(opp, false);
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Mark as rejected to improve future recommendation calibration.");
                ImGui.TableNextColumn(); ImGui.Text(opp.ScannedUtc.ToLocalTime().ToString("HH:mm:ss"));
                if (opportunityDetailedTooltips && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Timestamp when this opportunity was evaluated.");
            }

            ImGui.EndTable();
        }

        private void RefreshWatchlist()
        {
            try { cachedWatchlist = scanner.GetWatchlist().ToList(); }
            catch { cachedWatchlist = new List<WatchedItem>(); }
        }

        private void RefreshCapitalPlan()
        {
            capitalPlan = scanner.GetCapitalAllocationPlan(latestResults, 20).ToList();
            capitalSimulation = scanner.SimulateOpportunityQueue(capitalPlan);
        }

        private async Task RefreshExclusionInspectorAsync(bool force)
        {
            if (exclusionRefreshInProgress)
                return;

            var now = DateTime.UtcNow;
            var cacheTtl = TimeSpan.FromSeconds(90);
            var debounce = TimeSpan.FromSeconds(2);

            if (!force && exclusionReasons.Count > 0 && (now - lastExclusionRefreshUtc) < cacheTtl)
            {
                exclusionStatus = $"Using cached exclusions ({exclusionReasons.Count})";
                return;
            }

            if (force && (now - lastExclusionRequestUtc) < debounce)
            {
                exclusionStatus = "Please wait before refreshing exclusions again.";
                return;
            }

            exclusionRefreshInProgress = true;
            lastExclusionRequestUtc = now;
            exclusionStatus = "Analyzing excluded watchlist items...";
            try
            {
                exclusionReasons = (await scanner.ExplainWatchlistExclusionsAsync(CancellationToken.None)).ToList();
                lastExclusionRefreshUtc = DateTime.UtcNow;
                exclusionStatus = exclusionReasons.Count == 0
                    ? "No exclusions detected"
                    : $"{exclusionReasons.Count} excluded watchlist items analyzed";
            }
            catch (Exception ex)
            {
                exclusionStatus = $"Exclusion analysis failed: {ex.Message}";
            }
            finally
            {
                exclusionRefreshInProgress = false;
            }
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
                RefreshCapitalPlan();
                _ = RefreshExclusionInspectorAsync(force: false);
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
