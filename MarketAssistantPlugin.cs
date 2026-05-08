using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UndercutterFFXIV.Services;
using UndercutterFFXIV.Windows;

namespace UndercutterFFXIV
{
    public sealed class MarketAssistantPlugin : IDalamudPlugin
    {
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
        [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

        private const string CommandName = "/ma";

        public Configuration Configuration { get; }
        public RetainerPriceService RetainerPriceService { get; }

        private readonly WindowSystem windowSystem = new("MarketMasterPro");
        private readonly MarketMasterWindow mainWindow;
        private readonly ProfitScannerService scanner;
        private readonly Random random = new();
        private readonly DateTime sessionStartedUtc = DateTime.UtcNow;

        private System.Timers.Timer? backgroundTimer;

        public MarketAssistantPlugin()
        {
            LoggingService.Initialize(PluginInterface.GetPluginConfigDirectory());

            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            var database = new MarketMasterDatabase(PluginInterface.GetPluginConfigDirectory());
            var http = new HttpClient();
            var universalis = new UniversalisMarketClient(http);
            RetainerPriceService = new RetainerPriceService(GameGui, DataManager);
            scanner = new ProfitScannerService(DataManager, universalis, database, Configuration, RetainerPriceService);

            mainWindow = new MarketMasterWindow(this, scanner);
            windowSystem.AddWindow(mainWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Market Master Pro | /ma scan | /ma scanner"
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;

            RefreshBackgroundPolling();
            LoggingService.LogInfo("Market Master Pro initialized");
        }

        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;

            if (backgroundTimer != null)
            {
                backgroundTimer.Stop();
                backgroundTimer.Dispose();
                backgroundTimer = null;
            }

            windowSystem.RemoveAllWindows();
            CommandManager.RemoveHandler(CommandName);
        }

        public void RefreshBackgroundPolling()
        {
            if (backgroundTimer != null)
            {
                backgroundTimer.Stop();
                backgroundTimer.Dispose();
                backgroundTimer = null;
            }

            if (!Configuration.EnableBackgroundPolling)
                return;

            backgroundTimer = new System.Timers.Timer
            {
                AutoReset = false,
                Interval = 1000
            };

            backgroundTimer.Elapsed += async (_, _) =>
            {
                try
                {
                    if (Configuration.EnableSessionLimit)
                    {
                        var max = Math.Max(1, Configuration.SessionLimitHours);
                        if ((DateTime.UtcNow - sessionStartedUtc) > TimeSpan.FromHours(max))
                        {
                            LoggingService.LogInfo("Background scanner paused due to session limit");
                            return;
                        }
                    }

                    await scanner.ScanWatchlistOnlyAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Background scan failed: {ex.Message}");
                }
                finally
                {
                    ScheduleNextBackgroundTick();
                }
            };

            ScheduleNextBackgroundTick();
        }

        public void ToggleMainUi()
        {
            mainWindow.OnOpen();
            mainWindow.IsOpen = true;
        }

        private void DrawUI()
        {
            windowSystem.Draw();
        }

        private void ScheduleNextBackgroundTick()
        {
            if (backgroundTimer == null)
                return;

            var baseSeconds = Math.Max(30, Configuration.PollingBaseSeconds);
            var jitter = Math.Max(0, Configuration.PollingJitterSeconds);
            var offset = random.Next(-jitter, jitter + 1);
            var seconds = Math.Max(15, baseSeconds + offset);

            backgroundTimer.Interval = seconds * 1000;
            backgroundTimer.Start();
        }

        private void OnCommand(string command, string args)
        {
            var normalized = (args ?? string.Empty).Trim().ToLowerInvariant();

            if (normalized == "scan" || normalized == "sync")
            {
                _ = scanner.ScanWatchlistOnlyAsync(CancellationToken.None);
                mainWindow.OnOpen();
                mainWindow.IsOpen = true;
                return;
            }

            if (normalized == "scanner")
            {
                mainWindow.OpenScanner();
                return;
            }

            mainWindow.OnOpen();
            mainWindow.IsOpen = true;
        }
    }
}
