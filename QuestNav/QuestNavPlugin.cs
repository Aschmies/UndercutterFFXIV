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
        [PluginService] internal static IPluginLog Log                        { get; private set; } = null!;

        private const string CommandName = "/questnav";

        public Configuration Configuration { get; }

        private readonly WindowSystem windowSystem = new("QuestNav");
        private readonly QuestNavWindow mainWindow;
        private readonly QuestService questService;

        public QuestNavPlugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            questService = new QuestService(DataManager, AetheryteList, Log);
            mainWindow = new QuestNavWindow(questService, ClientState);
            windowSystem.AddWindow(mainWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open QuestNav — one-click teleport to quest locations. | /questnav"
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        }

        private void OnCommand(string command, string args) => ToggleMainUi();

        private void DrawUI() => windowSystem.Draw();

        public void ToggleMainUi() => mainWindow.Toggle();

        public void Dispose()
        {
            CommandManager.RemoveHandler(CommandName);
            windowSystem.RemoveAllWindows();
        }
    }
}
