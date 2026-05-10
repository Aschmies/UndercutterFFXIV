using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using QuestNav.Services;
using QuestNav.Windows;

namespace QuestNav
{
    public sealed class QuestNavPlugin : IDalamudPlugin
    {
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager        { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager              { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState              { get; private set; } = null!;
        [PluginService] internal static IAetheryteList AetheryteList          { get; private set; } = null!;
        [PluginService] internal static IGameGui GameGui                      { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log                        { get; private set; } = null!;
        [PluginService] internal static IFramework Framework                  { get; private set; } = null!;

        private const string CommandName = "/questnav";

        private int lastQuestCount = -1;
        private DateTime lastEventRefresh = DateTime.MinValue;

        public Configuration Configuration { get; }

        private readonly WindowSystem windowSystem = new("QuestNav");
        private readonly QuestNavWindow mainWindow;
        private readonly ArrowOverlayWindow arrowOverlay;
        private readonly QuestService questService;

        public QuestNavPlugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            questService  = new QuestService(DataManager, AetheryteList, Log);
            arrowOverlay  = new ArrowOverlayWindow(Configuration, ClientState);
            mainWindow    = new QuestNavWindow(questService, ClientState, GameGui, Configuration, arrowOverlay);

            windowSystem.AddWindow(mainWindow);
            windowSystem.AddWindow(arrowOverlay);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open QuestNav — one-click teleport to quest locations. | /questnav"
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
            Framework.Update += OnFrameworkUpdate;
        }

        private void OnCommand(string command, string args) => ToggleMainUi();

        private void OnFrameworkUpdate(IFramework framework)
        {
            // Detect quest changes and refresh immediately
            if (!ClientState.IsLoggedIn)
                return;

            var currentQuests = questService.GetActiveQuests();
            bool questsChanged = currentQuests.Count != lastQuestCount;
            lastQuestCount = currentQuests.Count;

            if (questsChanged && (DateTime.UtcNow - lastEventRefresh).TotalMilliseconds > 500)
            {
                mainWindow.TriggerRefresh();
                lastEventRefresh = DateTime.UtcNow;
            }
        }

        private void DrawUI() => windowSystem.Draw();

        public void ToggleMainUi() => mainWindow.Toggle();

        public void Dispose()
        {
            CommandManager.RemoveHandler(CommandName);
            Framework.Update -= OnFrameworkUpdate;
            windowSystem.RemoveAllWindows();
        }
    }
}

