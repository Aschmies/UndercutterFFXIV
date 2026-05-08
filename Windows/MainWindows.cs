using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UndercutterFFXIV.Models;
using UndercutterFFXIV.Services;

namespace UndercutterFFXIV.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly MarketAssistantPlugin plugin;
        private Configuration Config => plugin.Configuration;

        // All public FFXIV worlds grouped by data centre
        private static readonly (string DC, string[] Worlds)[] DataCentres =
        [
            // NA
            ("Aether",    ["Adamantoise","Cactuar","Faerie","Gilgamesh","Jenova","Midgardsormr","Sargatanas","Siren"]),
            ("Crystal",   ["Balmung","Brynhildr","Coeurl","Diabolos","Goblin","Malboro","Mateus","Zalera"]),
            ("Dynamis",   ["Halicarnassus","Maduin","Marilith","Seraph"]),
            ("Primal",    ["Behemoth","Excalibur","Exodus","Famfrit","Hyperion","Lamia","Leviathan","Ultros"]),
            // EU
            ("Chaos",     ["Cerberus","Louisoix","Moogle","Omega","Phantom","Ragnarok","Sagittarius","Spriggan"]),
            ("Light",     ["Alpha","Lich","Odin","Phoenix","Raiden","Shiva","Twintania","Zodiark"]),
            ("Shadow",    ["Innocence","Pixie","Titania","Tycoon"]),
            // JP
            ("Elemental", ["Aegis","Atomos","Carbuncle","Garuda","Gungnir","Kujata","Tonberry","Typhon"]),
            ("Gaia",      ["Alexander","Bahamut","Durandal","Fenrir","Ifrit","Ridill","Tiamat","Ultima"]),
            ("Mana",      ["Anima","Asura","Chocobo","Hades","Ixion","Masamune","Pandaemonium","Titan"]),
            ("Meteor",    ["Bismarck","Carlsbreeze","Cieldalaes","Mandragora","Ramuh","Unicorn","Valefor","Yojimbo","Zeromus"]),
            // OCE
            ("Materia",   ["Bismarck","Ravana","Sephirot","Sophia","Zurvan"]),
        ];

        // Flat list + index lookup built once
        private static readonly string[] AllWorlds;
        private static readonly string[] AllDCLabels; // parallel array for display
        private int selectedWorldIndex;

        static ConfigWindow()
        {
            var worlds = new List<string>();
            var labels = new List<string>();
            foreach (var (dc, ws) in DataCentres)
                foreach (var w in ws)
                { worlds.Add(w); labels.Add($"{w}  ({dc})"); }
            AllWorlds  = worlds.ToArray();
            AllDCLabels = labels.ToArray();
        }

        public ConfigWindow(MarketAssistantPlugin plugin) : base("Settings###ConfigWindow")
        {
            this.plugin = plugin;
            Size = new Vector2(620, 540);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void OnOpen()
        {
            // Sync dropdown to whatever is saved
            selectedWorldIndex = Array.IndexOf(AllWorlds, Config.WorldName);
            if (selectedWorldIndex < 0) selectedWorldIndex = 0;
        }

        public override void Draw()
        {
            if (ImGui.BeginTabBar("##cfgTabs"))
            {
                if (ImGui.BeginTabItem("General##cfgGen")) { DrawGeneralTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Universalis##cfgUni")) { DrawUniversalisTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Alerts##cfgAlt")) { DrawAlertsTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Flipping##cfgFlip")) { DrawFlippingTab(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
        }

        private void DrawGeneralTab()
        {
            ImGui.Spacing();
            var enabled = Config.PluginEnabled;
            if (ImGui.Checkbox("Plugin enabled##cfgEnabled", ref enabled))
            { Config.PluginEnabled = enabled; Config.Save(); }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Market Board");
            ImGui.Spacing();

            var undercut = (int)Config.UndercutAmount;
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputInt("Undercut amount (gil)##cfgUndercut", ref undercut))
            { Config.UndercutAmount = (uint)Math.Max(1, undercut); Config.Save(); }
            ImGui.SameLine(); ImGui.TextDisabled("How much below the lowest listing to price your items");

            var tax = (float)Config.TaxPercentage;
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderFloat("Market tax %%##cfgTax", ref tax, 0f, 10f, "%.1f%%"))
            { Config.TaxPercentage = Math.Round(tax, 1); Config.Save(); }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("History");
            ImGui.Spacing();

            var histDays = Config.HistoryDisplayDays;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Display days##cfgHistDays", ref histDays))
            { Config.HistoryDisplayDays = Math.Clamp(histDays, 1, 365); Config.Save(); }

            var show = Config.ShowPriceHistory;
            if (ImGui.Checkbox("Show price history charts##cfgShowHist", ref show))
            { Config.ShowPriceHistory = show; Config.Save(); }

            var showMargin = Config.ShowProfitMargins;
            if (ImGui.Checkbox("Show profit margins##cfgShowMargin", ref showMargin))
            { Config.ShowProfitMargins = showMargin; Config.Save(); }
        }

        private void DrawUniversalisTab()
        {
            ImGui.Spacing();

            var useApi = Config.UseUniversalisAPI;
            if (ImGui.Checkbox("Enable Universalis price fetching##cfgUseApi", ref useApi))
            { Config.UseUniversalisAPI = useApi; Config.Save(); }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("World");
            ImGui.TextWrapped("Select the world your character is on. Universalis data is per-world.");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(280);
            if (ImGui.Combo("##worldCombo", ref selectedWorldIndex, AllDCLabels, AllDCLabels.Length))
            {
                Config.WorldName = AllWorlds[selectedWorldIndex];
                Config.Save();
            }
            ImGui.SameLine();
            ImGui.Text($"Selected: {Config.WorldName}");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Auto-Refresh");
            ImGui.Spacing();

            var refreshMins = Config.UniversalisRefreshMinutes;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Refresh interval (minutes)##cfgRefresh", ref refreshMins))
            { Config.UniversalisRefreshMinutes = Math.Max(1, refreshMins); Config.Save(); }
            ImGui.SameLine(); ImGui.TextDisabled("Minimum 1 minute");
        }

        private void DrawAlertsTab()
        {
            ImGui.Spacing();

            var chat = Config.EnableChatAlerts;
            if (ImGui.Checkbox("Chat notifications##cfgChat", ref chat))
            { Config.EnableChatAlerts = chat; Config.Save(); }

            var toast = Config.EnableToastNotifications;
            if (ImGui.Checkbox("Toast notifications##cfgToast", ref toast))
            { Config.EnableToastNotifications = toast; Config.Save(); }

            var undercut = Config.AlertOnUndercut;
            if (ImGui.Checkbox("Alert when undercut##cfgUnderAlert", ref undercut))
            { Config.AlertOnUndercut = undercut; Config.Save(); }

            ImGui.Spacing();
            ImGui.Separator();

            var maxDays = Config.MaxAlertHistoryDays;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Keep alerts for (days)##cfgAlertDays", ref maxDays))
            { Config.MaxAlertHistoryDays = Math.Clamp(maxDays, 1, 90); Config.Save(); }
        }

        private void DrawFlippingTab()
        {
            ImGui.Spacing();

            var sell = Config.EnableSellSuggestions;
            if (ImGui.Checkbox("Enable sell suggestions##cfgSell", ref sell))
            { Config.EnableSellSuggestions = sell; Config.Save(); }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var minGil = (int)Config.DefaultMinProfitGil;
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputInt("Default min profit (gil)##cfgMinGil", ref minGil))
            { Config.DefaultMinProfitGil = (uint)Math.Max(0, minGil); Config.Save(); }

            var minPct = (int)Config.DefaultMinProfitPercent;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Default min profit %%##cfgMinPct", ref minPct))
            { Config.DefaultMinProfitPercent = (uint)Math.Clamp(minPct, 0, 100); Config.Save(); }
        }

        public void Dispose() { }
    }

    public class SearchWindow : Window, IDisposable
    {
        private MarketTracker tracker { get; }
        private FlipTrackerService flipTracker { get; }
        private Configuration config { get; }

        private string searchItemName = "";
        private int searchItemId;
        private int targetBuyPrice;
        private int targetSellPrice;
        private string statusMessage = "";
        private bool isFetching;

        public SearchWindow(MarketTracker tracker, FlipTrackerService flipTracker, Configuration config)
            : base("Item Search & Watchlist###SearchWindow")
        {
            this.tracker = tracker;
            this.flipTracker = flipTracker;
            this.config = config;
            Size = new Vector2(740, 520);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            if (ImGui.BeginTabBar("##searchTabs"))
            {
                if (ImGui.BeginTabItem("Add Item##searchAdd"))
                {
                    DrawAddItemTab();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Watchlist##searchWatch"))
                {
                    DrawWatchlistTab();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }

        private void DrawAddItemTab()
        {
            ImGui.TextWrapped("Add any item by ID and name — no market board visit needed. Prices are fetched directly from Universalis.");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushItemWidth(180);
            ImGui.InputInt("Item ID##searchId", ref searchItemId);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(280);
            ImGui.InputText("Item Name##searchName", ref searchItemName, 128);
            ImGui.PopItemWidth();

            ImGui.PushItemWidth(180);
            ImGui.InputInt("Target Buy Price##searchBuy", ref targetBuyPrice);
            ImGui.SameLine();
            ImGui.InputInt("Target Sell Price##searchSell", ref targetSellPrice);
            ImGui.PopItemWidth();

            ImGui.Spacing();

            var canSubmit = searchItemId > 0 && !string.IsNullOrWhiteSpace(searchItemName) && !isFetching;
            if (!canSubmit) ImGui.BeginDisabled();

            if (ImGui.Button("Add & Fetch Prices##searchAddFetch"))
            {
                var id = (uint)searchItemId;
                var name = searchItemName.Trim();
                var buy = (uint)Math.Max(0, targetBuyPrice);
                var sell = (uint)Math.Max(0, targetSellPrice);
                flipTracker.AddToWatchlist(id, name, buy, sell);

                if (!string.IsNullOrWhiteSpace(config.WorldName))
                {
                    isFetching = true;
                    statusMessage = $"Fetching prices for '{name}'...";
                    _ = FetchAndUpdateStatus(id, name);
                }
                else
                {
                    statusMessage = $"Added '{name}' (ID {id}) — set WorldName in settings to fetch prices.";
                }

                searchItemId = 0;
                searchItemName = "";
                targetBuyPrice = 0;
                targetSellPrice = 0;
            }

            ImGui.SameLine();

            if (ImGui.Button("Add Only##searchAddOnly") && searchItemId > 0 && !string.IsNullOrWhiteSpace(searchItemName))
            {
                flipTracker.AddToWatchlist(
                    (uint)searchItemId,
                    searchItemName.Trim(),
                    (uint)Math.Max(0, targetBuyPrice),
                    (uint)Math.Max(0, targetSellPrice));
                statusMessage = $"Added '{searchItemName.Trim()}' (ID {searchItemId}) to watchlist.";
                searchItemId = 0;
                searchItemName = "";
                targetBuyPrice = 0;
                targetSellPrice = 0;
            }

            if (!canSubmit) ImGui.EndDisabled();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                ImGui.Spacing();
                var color = statusMessage.StartsWith("Fetching") 
                    ? new Vector4(1f, 0.85f, 0.3f, 1f) 
                    : new Vector4(0.4f, 1f, 0.4f, 1f);
                ImGui.TextColored(color, statusMessage);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("Tip: Item IDs can be found on the FFXIV wiki or by searching on Universalis.app");
        }

        private async Task FetchAndUpdateStatus(uint itemId, string itemName)
        {
            var success = await tracker.FetchItemFromUniversalis(config.WorldName, itemId, itemName);
            statusMessage = success
                ? $"\u2713 Prices loaded for '{itemName}'"
                : $"\u2717 Could not fetch prices for '{itemName}' — check item ID and world name";
            isFetching = false;
        }

        private void DrawWatchlistTab()
        {
            var watchlist = flipTracker.GetWatchlist();

            if (watchlist.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("No items on watchlist. Add items in the \"Add Item\" tab.");
                return;
            }

            ImGui.Text($"{watchlist.Count} item(s) on watchlist");
            ImGui.Separator();

            if (ImGui.BeginTable("##watchlistTable", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, -30)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Target Buy", ImGuiTableColumnFlags.WidthFixed, 95);
                ImGui.TableSetupColumn("Curr. Price", ImGuiTableColumnFlags.WidthFixed, 95);
                ImGui.TableSetupColumn("Trend", ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableSetupColumn("Target Sell", ImGuiTableColumnFlags.WidthFixed, 95);
                ImGui.TableSetupColumn("##remove", ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableHeadersRow();

                uint? toRemove = null;
                foreach (var item in watchlist)
                {
                    var history = tracker.GetPriceHistory(item.ItemId, 7);
                    var currentPrice = history.Count > 0 ? history.Last().LowestPrice : 0;
                    var trend = GetTrendString(history);
                    var atTarget = currentPrice > 0 && item.TargetBuyPrice > 0 && currentPrice <= item.TargetBuyPrice;

                    ImGui.TableNextRow();

                    if (atTarget)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                            ImGui.GetColorU32(new Vector4(0.15f, 0.5f, 0.15f, 0.45f)));

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text(item.ItemName);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextDisabled($"{item.ItemId}");

                    ImGui.TableSetColumnIndex(2);
                    ImGui.Text(item.TargetBuyPrice > 0 ? $"{item.TargetBuyPrice:N0}" : "—");

                    ImGui.TableSetColumnIndex(3);
                    if (currentPrice == 0)
                        ImGui.TextDisabled("No data");
                    else if (atTarget)
                        ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"{currentPrice:N0} \u2714");
                    else
                        ImGui.Text($"{currentPrice:N0}");

                    ImGui.TableSetColumnIndex(4);
                    var trendColor = trend == "Rising"  ? new Vector4(1f, 0.4f, 0.4f, 1f) :
                                     trend == "Falling" ? new Vector4(0.4f, 1f, 0.4f, 1f) :
                                                         new Vector4(0.65f, 0.65f, 0.65f, 1f);
                    ImGui.TextColored(trendColor, trend);

                    ImGui.TableSetColumnIndex(5);
                    ImGui.Text(item.TargetSellPrice > 0 ? $"{item.TargetSellPrice:N0}" : "—");

                    ImGui.TableSetColumnIndex(6);
                    if (ImGui.SmallButton($"Remove##{item.ItemId}"))
                        toRemove = item.ItemId;
                }

                ImGui.EndTable();
                if (toRemove.HasValue)
                    flipTracker.RemoveFromWatchlist(toRemove.Value);
            }

            ImGui.TextDisabled("Green rows = current price is at or below your target buy price.");
        }

        private string GetTrendString(List<MarketPriceSnapshot> history)
        {
            if (history.Count < 2) return "—";
            var first = history.First().LowestPrice;
            var last = history.Last().LowestPrice;
            if (last > first * 1.05) return "Rising";
            if (last < first * 0.95) return "Falling";
            return "Stable";
        }

        public void Dispose() { }
    }

    public class PriceHistoryWindow : Window, IDisposable
    {
        private readonly MarketTracker tracker;
        private readonly FlipTrackerService flipTracker;
        private List<(uint Id, string Name)> itemList = new();
        private int selectedItemIndex = 0;
        private int displayDays = 30;

        public PriceHistoryWindow(MarketTracker tracker, FlipTrackerService flipTracker)
            : base("Price History###PriceHistoryWindow")
        {
            this.tracker = tracker;
            this.flipTracker = flipTracker;
            Size = new Vector2(820, 520);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void OnOpen() => RefreshItemList();

        private void RefreshItemList()
        {
            var seen = new HashSet<uint>();
            itemList.Clear();
            foreach (var i in tracker.GetListedItems())
                if (seen.Add(i.ItemId)) itemList.Add((i.ItemId, i.ItemName));
            foreach (var w in flipTracker.GetWatchlist())
                if (seen.Add(w.ItemId)) itemList.Add((w.ItemId, w.ItemName));
            if (selectedItemIndex >= itemList.Count) selectedItemIndex = 0;
        }

        public override void Draw()
        {
            if (ImGui.Button("Refresh##phRefresh")) RefreshItemList();

            if (itemList.Count == 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("No tracked items. Add via /ma search or visit the market board.");
                return;
            }

            ImGui.SameLine();
            var names = itemList.Select(i => $"{i.Name}  (ID {i.Id})").ToArray();
            ImGui.SetNextItemWidth(300);
            ImGui.Combo("##phItemCombo", ref selectedItemIndex, names, names.Length);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(130);
            ImGui.SliderInt("Days##phDays", ref displayDays, 1, 90);
            ImGui.Separator();

            var (itemId, itemName) = itemList[selectedItemIndex];
            var history = tracker.GetPriceHistory(itemId, displayDays);

            if (history.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f),
                    "No price data yet. Use /ma sync to fetch prices from Universalis.");
                return;
            }

            var minP = history.Min(h => h.LowestPrice);
            var maxP = history.Max(h => h.LowestPrice);
            var avgP = (uint)history.Average(h => h.AveragePrice);
            ImGui.Text($"Range: {minP:N0} \u2013 {maxP:N0} gil   |   Avg: {avgP:N0} gil   |   {history.Count} snapshots");
            ImGui.Separator();

            if (ImGui.BeginTable("##phTable", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Date / Time",  ImGuiTableColumnFlags.WidthFixed, 140);
                ImGui.TableSetupColumn("Lowest",       ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Average",      ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Median",       ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Qty Listed",   ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableHeadersRow();

                foreach (var snap in history.OrderByDescending(h => h.Timestamp))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(snap.Timestamp.ToString("MM/dd HH:mm"));
                    ImGui.TableNextColumn(); ImGui.Text($"{snap.LowestPrice:N0}");
                    ImGui.TableNextColumn(); ImGui.Text($"{snap.AveragePrice:N0}");
                    ImGui.TableNextColumn(); ImGui.Text($"{snap.MedianPrice:N0}");
                    ImGui.TableNextColumn(); ImGui.Text(snap.QuantityListed.ToString());
                }
                ImGui.EndTable();
            }
        }

        public void Dispose() { }
    }

    public class ProfitAnalysisWindow : Window, IDisposable
    {
        private readonly MarketTracker tracker;
        private readonly FlipTrackerService flipTracker;
        private int statsDays = 30;

        public ProfitAnalysisWindow(MarketTracker tracker, FlipTrackerService flipTracker)
            : base("Profit Analysis###ProfitAnalysisWindow")
        {
            this.tracker = tracker;
            this.flipTracker = flipTracker;
            Size = new Vector2(820, 560);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            ImGui.SetNextItemWidth(130);
            ImGui.SliderInt("Period (days)##paDays", ref statsDays, 1, 90);
            ImGui.SameLine();
            ImGui.TextDisabled($"Last {statsDays} days");
            ImGui.Separator();

            if (ImGui.BeginTabBar("##paTabs"))
            {
                if (ImGui.BeginTabItem("Summary##paSummary"))    { DrawSummaryTab();      ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Per Item##paPerItem"))   { DrawPerItemTab();      ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Transactions##paTrans")) { DrawTransactionsTab(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
        }

        private void DrawSummaryTab()
        {
            var stats = flipTracker.GetStatistics(statsDays);
            ImGui.Spacing();

            if (stats.TotalFlips == 0)
            {
                ImGui.TextDisabled("No completed flips recorded in this period.");
                ImGui.TextDisabled("Complete a flip via /ma active to start tracking profits.");
                return;
            }

            if (ImGui.BeginTable("##paSumTable", 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, 160);
                ImGui.TableSetupColumn("##value");

                void Row(string label, string value, Vector4? color = null)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(label);
                    ImGui.TableNextColumn();
                    if (color.HasValue) ImGui.TextColored(color.Value, value);
                    else ImGui.Text(value);
                }

                Row("Total Flips:",    stats.TotalFlips.ToString());
                Row("Total Profit:",   $"{stats.TotalProfit:N0} gil",  new Vector4(0.4f, 1f, 0.4f, 1f));
                Row("Total Volume:",   $"{stats.TotalVolume:N0} gil");
                Row("Avg Profit %:",   $"{stats.AverageProfitPercentage:F1}%");
                Row("Avg Hold Time:",  $"{stats.AverageHoldingHours:F1}h");
                if (stats.MostFlippedItemId != 0)
                    Row("Best Item:",  stats.MostFlippedItemName);
                if (stats.HighestSingleProfit > 0)
                    Row("Best Flip:",  $"{stats.HighestSingleProfit:N0} gil", new Vector4(1f, 0.85f, 0.2f, 1f));

                ImGui.EndTable();
            }
        }

        private void DrawPerItemTab()
        {
            var perItem = flipTracker.GetPerItemStats(statsDays);
            var transactions = flipTracker.GetTransactions(statsDays);

            if (perItem.Count == 0)
            {
                ImGui.TextDisabled("No completed flips recorded in this period.");
                return;
            }

            if (ImGui.BeginTable("##paItemTable", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Flips",        ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Total Profit", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Avg Margin",   ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableHeadersRow();

                foreach (var kv in perItem.OrderByDescending(k => k.Value.profit))
                {
                    var name = transactions.FirstOrDefault(t => t.ItemId == kv.Key)?.ItemName
                               ?? $"ID {kv.Key}";
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(name);
                    ImGui.TableNextColumn(); ImGui.Text(kv.Value.count.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"{kv.Value.profit:N0}");
                    ImGui.TableNextColumn(); ImGui.Text($"{kv.Value.margin:F1}%");
                }
                ImGui.EndTable();
            }
        }

        private void DrawTransactionsTab()
        {
            var transactions = flipTracker.GetTransactions(statsDays);

            if (transactions.Count == 0)
            {
                ImGui.TextDisabled("No completed flips in this period.");
                return;
            }

            if (ImGui.BeginTable("##paTransTable", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Date");
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Bought",     ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Sold",       ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Qty",        ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Net Profit", ImGuiTableColumnFlags.WidthFixed, 105);
                ImGui.TableHeadersRow();

                foreach (var t in transactions)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(t.SellTime.ToString("MM/dd HH:mm"));
                    ImGui.TableNextColumn(); ImGui.Text(t.ItemName);
                    ImGui.TableNextColumn(); ImGui.Text($"{t.BuyPrice:N0}");
                    ImGui.TableNextColumn(); ImGui.Text($"{t.SellPrice:N0}");
                    ImGui.TableNextColumn(); ImGui.Text(t.Quantity.ToString());
                    ImGui.TableNextColumn();
                    var color = t.NetProfit > 0
                        ? new Vector4(0.4f, 1f, 0.4f, 1f)
                        : new Vector4(1f, 0.4f, 0.4f, 1f);
                    ImGui.TextColored(color, $"{t.NetProfit:N0}");
                }
                ImGui.EndTable();
            }
        }

        public void Dispose() { }
    }

    public class BulkAdjustWindow : Window, IDisposable
    {
        private readonly MarketTracker tracker;
        private readonly MarketAssistantPlugin plugin;
        private Configuration Config => plugin.Configuration;

        public BulkAdjustWindow(MarketAssistantPlugin plugin, MarketTracker tracker)
            : base("Bulk Price Adjustment###BulkAdjustWindow")
        {
            this.plugin = plugin;
            this.tracker = tracker;
            Size = new Vector2(820, 500);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public void Open() { IsOpen = true; }

        public override void Draw()
        {
            ImGui.TextWrapped($"Suggested undercut prices (undercutting by {Config.UndercutAmount:N0} gil). Use this as a reference when adjusting prices at the market board.");
            ImGui.Spacing();

            var items = tracker.GetListedItems();
            if (items.Count == 0)
            {
                ImGui.TextDisabled("No tracked items. Visit the market board or add items to the watchlist via /ma search.");
                return;
            }

            if (ImGui.BeginTable("##baTable", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Your Price",  ImGuiTableColumnFlags.WidthFixed, 105);
                ImGui.TableSetupColumn("Market Low",  ImGuiTableColumnFlags.WidthFixed, 105);
                ImGui.TableSetupColumn("Suggested",   ImGuiTableColumnFlags.WidthFixed, 105);
                ImGui.TableSetupColumn("Status",      ImGuiTableColumnFlags.WidthFixed, 115);
                ImGui.TableHeadersRow();

                foreach (var item in items.OrderBy(i => i.ItemName))
                {
                    var marketLow = item.CurrentLowestPrice;
                    var suggested = marketLow > Config.UndercutAmount
                        ? marketLow - Config.UndercutAmount
                        : marketLow;
                    var isUndercut = marketLow > 0 && item.ListedPrice > marketLow;
                    var isOptimal  = marketLow > 0 && item.ListedPrice == suggested;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(item.ItemName);
                    ImGui.TableNextColumn(); ImGui.Text($"{item.ListedPrice:N0}");
                    ImGui.TableNextColumn();
                    if (marketLow > 0) ImGui.Text($"{marketLow:N0}");
                    else ImGui.TextDisabled("Unknown");
                    ImGui.TableNextColumn();
                    if (suggested > 0) ImGui.Text($"{suggested:N0}");
                    else ImGui.TextDisabled("\u2014");
                    ImGui.TableNextColumn();
                    if (marketLow == 0)
                        ImGui.TextDisabled("No data");
                    else if (isUndercut)
                        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Undercut!");
                    else if (isOptimal)
                        ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "Optimal");
                    else
                        ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "Adjust down");
                }
                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Red = undercut by someone.  Yellow = you can price lower.  Green = already optimal.");
        }

        public void Dispose() { }
    }
}
