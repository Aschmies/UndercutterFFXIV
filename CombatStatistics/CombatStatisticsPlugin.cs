using CombatStatistics.Config;
using CombatStatistics.Services;
using CombatStatistics.UI;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CombatStatistics;

public sealed class CombatStatisticsPlugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/combatstats";

    private readonly WindowSystem windowSystem = new("CombatStatistics");
    private readonly CombatStatisticsTracker tracker;
    private readonly CombatStatisticsWindow mainWindow;
    private readonly CombatStatisticsOverlayWindow overlayWindow;

    public Configuration Configuration { get; }

    public CombatStatisticsPlugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        tracker = new CombatStatisticsTracker(Configuration);
        mainWindow = new CombatStatisticsWindow(Configuration, tracker);
        overlayWindow = new CombatStatisticsOverlayWindow(Configuration, tracker);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(overlayWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Combat Statistics settings. | /combatstats"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        Framework.Update += OnFrameworkUpdate;

        if (Configuration.ShowMainWindowOnStart)
            mainWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args) => ToggleMainUi();

    private void OnFrameworkUpdate(IFramework framework)
    {
        tracker.Update(ObjectTable, PartyList, ClientState);
        overlayWindow.IsOpen = Configuration.ShowOverlay && tracker.HasActiveEncounter;
    }

    private void DrawUI() => windowSystem.Draw();

    private void ToggleMainUi() => mainWindow.Toggle();

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
    }
}
