using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ArmouryCleaner.Services;
using ArmouryCleaner.Windows;

namespace ArmouryCleaner
{
    public sealed class ArmouryCleanerPlugin : IDalamudPlugin
    {
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
        [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
        [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;

        private const string CommandName = "/ac";

        public Configuration Configuration { get; }

        private readonly WindowSystem windowSystem = new("ArmouryCleaner");
        private readonly ArmouryCleanerWindow mainWindow;
        private readonly ArmouryService armouryService;

        public ArmouryCleanerPlugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            armouryService = new ArmouryService(DataManager, ClientState, Log);
            mainWindow = new ArmouryCleanerWindow(this, armouryService);
            windowSystem.AddWindow(mainWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Armoury Cleaner | /ac"
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
            mainWindow.Dispose();
        }
    }
}
