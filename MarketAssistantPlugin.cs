using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using UndercutterFFXIV.Services;
using UndercutterFFXIV.Windows;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace UndercutterFFXIV
{
    public sealed class MarketAssistantPlugin : IDalamudPlugin
    {
        // ── Dalamud services injected statically ──────────────────────────────
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

        private const string CommandName = "/ma";

        public Configuration Configuration { get; init; }
        private WindowSystem WindowSystem = new("UndercutterFFXIV");

        // Main windows
        private MarketBoardWindow MarketBoardWindow { get; init; }
        private ConfigWindow ConfigWindow { get; init; }
        private SearchWindow SearchWindow { get; init; }

        // Price adjustment windows
        private BulkAdjustWindow BulkAdjustWindow { get; init; }
        private PriceHistoryWindow PriceHistoryWindow { get; init; }
        private ProfitAnalysisWindow ProfitAnalysisWindow { get; init; }

        // Flipping windows
        private FlipOpportunitiesWindow FlipOpportunitiesWindow { get; init; }
        private FlipTrackerWindow FlipTrackerWindow { get; init; }
        private ActiveFlipsWindow ActiveFlipsWindow { get; init; }

        // Services
        private MarketTracker MarketTracker { get; init; }
        private PersistenceService PersistenceService { get; init; }
        private NotificationService NotificationService { get; init; }
        private RetainerSyncService RetainerSyncService { get; init; }
        private FlipAnalyzerService FlipAnalyzerService { get; init; }
        private FlipTrackerService FlipTrackerService { get; init; }
        private SellSuggestionService SellSuggestionService { get; init; }
        private System.Timers.Timer? refreshTimer;
        private DateTime lastSellSuggestionUpdate = DateTime.MinValue;

        public MarketAssistantPlugin()
        {
            // Initialize logging first so everything else can use it
            LoggingService.Initialize(PluginInterface.GetPluginConfigDirectory());

            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            // Initialize services
            MarketTracker = new MarketTracker(Configuration.UndercutAmount);
            PersistenceService = new PersistenceService(PluginInterface.GetPluginConfigDirectory());
            MarketTracker.InitializePersistence(PersistenceService);
            NotificationService = new NotificationService();
            RetainerSyncService = new RetainerSyncService(MarketTracker);
            FlipAnalyzerService = new FlipAnalyzerService(MarketTracker);
            FlipTrackerService = new FlipTrackerService();
            FlipTrackerService.InitializePersistence(PersistenceService);
            SellSuggestionService = new SellSuggestionService(MarketTracker);

            LoggingService.LogInfo("Market Assistant plugin initializing");

            // Register windows
            MarketBoardWindow = new MarketBoardWindow(this, MarketTracker, NotificationService);
            ConfigWindow = new ConfigWindow(this);
            BulkAdjustWindow = new BulkAdjustWindow(this, MarketTracker);
            PriceHistoryWindow = new PriceHistoryWindow(MarketTracker, FlipTrackerService);
            ProfitAnalysisWindow = new ProfitAnalysisWindow(MarketTracker, FlipTrackerService);
            SearchWindow = new SearchWindow(MarketTracker, FlipTrackerService, Configuration);
            FlipOpportunitiesWindow = new FlipOpportunitiesWindow(MarketTracker, FlipAnalyzerService, FlipTrackerService);
            FlipTrackerWindow = new FlipTrackerWindow(FlipTrackerService, FlipAnalyzerService, MarketTracker, SellSuggestionService);
            ActiveFlipsWindow = new ActiveFlipsWindow(SellSuggestionService, FlipTrackerService);

            WindowSystem.AddWindow(MarketBoardWindow);
            WindowSystem.AddWindow(ConfigWindow);
            WindowSystem.AddWindow(BulkAdjustWindow);
            WindowSystem.AddWindow(PriceHistoryWindow);
            WindowSystem.AddWindow(ProfitAnalysisWindow);
            WindowSystem.AddWindow(SearchWindow);
            WindowSystem.AddWindow(FlipOpportunitiesWindow);
            WindowSystem.AddWindow(FlipTrackerWindow);
            WindowSystem.AddWindow(ActiveFlipsWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Market Assistant window | /ma active | /ma flip | /ma tracker | /ma sync"
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

            // Start background Universalis price refresh
            if (Configuration.UseUniversalisAPI)
            {
                StartBackgroundRefresh();
            }

            LoggingService.LogInfo("Market Assistant plugin initialized successfully");
        }

        public void Dispose()
        {
            LoggingService.LogInfo("Market Assistant plugin disposing");

            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

            if (refreshTimer != null)
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
            }

            MarketTracker.SaveToPersistence();
            WindowSystem.RemoveAllWindows();
            CommandManager.RemoveHandler(CommandName);
        }

        private void OnCommand(string command, string args)
        {
            args = args.ToLower().Trim();

            if (args == "config")
                ConfigWindow.IsOpen = true;
            else if (args == "search")
                SearchWindow.IsOpen = true;
            else if (args == "history")
                PriceHistoryWindow.IsOpen = true;
            else if (args == "profit")
                ProfitAnalysisWindow.IsOpen = true;
            else if (args == "adjust")
                BulkAdjustWindow.Open();
            else if (args == "flip" || args == "flips")
            {
                var watchlistItems = FlipTrackerService.GetWatchlist().Select(w => (w.ItemId, w.ItemName));
                FlipAnalyzerService.AnalyzeForFlips(100, 5.0, watchlistItems);
                FlipOpportunitiesWindow.IsOpen = true;
            }
            else if (args == "tracker")
                FlipTrackerWindow.IsOpen = true;
            else if (args == "active" || args == "selling")
            {
                SellSuggestionService.UpdateMarketPrices();
                ActiveFlipsWindow.IsOpen = true;
            }
            else if (args.StartsWith("sync"))
            {
                var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var world = tokens.Length > 1 ? tokens[1] : ActiveWorldName;
                _ = SyncUniversalisAsync(world);
            }
            else
                MarketBoardWindow.IsOpen = true;
        }

        /// Returns the configured world name, used for all Universalis API calls.
        private string ActiveWorldName => Configuration.WorldName;

        public void ToggleMainUi() => MarketBoardWindow.Toggle();

        private void DrawConfigUI() => ConfigWindow.IsOpen = true;

        private void DrawUI()
        {
            // Keep sell suggestions fresh between background timer ticks (5-second throttle)
            if (Configuration.EnableSellSuggestions &&
                DateTime.Now - lastSellSuggestionUpdate > TimeSpan.FromSeconds(5))
            {
                SellSuggestionService.UpdateMarketPrices();
                lastSellSuggestionUpdate = DateTime.Now;
            }

            WindowSystem.Draw();
        }

        private async Task SyncUniversalisAsync(string worldName)
        {
            try
            {
                var watchlistItems = FlipTrackerService.GetWatchlist().Select(w => (w.ItemId, w.ItemName));
                await MarketTracker.RefreshPricesFromUniversalis(worldName, watchlistItems);
                SellSuggestionService.UpdateMarketPrices();
                NotificationService.SendChatNotification($"✓ Synced prices from Universalis ({worldName})");
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Manual sync error: {ex.Message}");
                NotificationService.SendChatNotification($"✗ Sync failed: {ex.Message}");
            }
        }

        private void StartBackgroundRefresh()
        {
            if (string.IsNullOrWhiteSpace(Configuration.WorldName))
            {
                LoggingService.LogWarning("WorldName not configured — skipping background Universalis refresh");
                return;
            }

            var minutes = Math.Max(1, Configuration.UniversalisRefreshMinutes); // clamp: min 1 minute
            var intervalMs = minutes * 60 * 1000;
            refreshTimer = new System.Timers.Timer(intervalMs);
            refreshTimer.Elapsed += async (sender, e) =>
            {
                try
                {
                    var watchlistItems = FlipTrackerService.GetWatchlist().Select(w => (w.ItemId, w.ItemName));
                    await MarketTracker.RefreshPricesFromUniversalis(ActiveWorldName, watchlistItems);
                    SellSuggestionService.UpdateMarketPrices();

                    var readyToSell = SellSuggestionService.GetSellReadyItems();
                    if (readyToSell.Count > 0)
                    {
                        NotificationService.SendChatNotification(
                            $"💰 {readyToSell.Count} item(s) ready to sell!");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Background refresh error: {ex.Message}");
                }
            };
            refreshTimer.AutoReset = true;
            refreshTimer.Start();

            LoggingService.LogInfo(
                $"Background price refresh started (every {Configuration.UniversalisRefreshMinutes} minutes)");
        }
    }
}
