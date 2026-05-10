using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Slidecaster.Windows;

namespace Slidecaster;

public sealed class SlidecasterPlugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/slidecaster";

    private readonly WindowSystem windowSystem = new("Slidecaster");
    private readonly CastbarOverlayWindow overlayWindow;
    private readonly SlidecasterWindow mainWindow;

    public Configuration Configuration { get; }

    public SlidecasterPlugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        overlayWindow = new CastbarOverlayWindow(Configuration, ObjectTable, GameGui, Log);
        mainWindow = new SlidecasterWindow(Configuration, overlayWindow);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(overlayWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Slidecaster settings. | /slidecaster"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        Framework.Update += OnFrameworkUpdate;
    }

    private void OnCommand(string command, string args) => ToggleMainUi();

    private void OnFrameworkUpdate(IFramework framework)
    {
        overlayWindow.UpdateCastState();
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
        overlayWindow.Dispose();
    }
}
