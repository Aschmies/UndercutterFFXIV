using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using BagAssistant.Services;
using BagAssistant.Windows;

namespace BagAssistant;

public sealed class BagAssistantPlugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/bagassistant";
    private const string ShortCommand = "/ba";

    public Configuration Configuration { get; }

    private readonly WindowSystem windowSystem = new("BagAssistant");
    private readonly BagAssistantWindow mainWindow;
    internal readonly InventoryService InventoryService;
    internal readonly SortRunner SortRunner;

    public BagAssistantPlugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        InventoryService = new InventoryService(DataManager, Log);
        SortRunner = new SortRunner(InventoryService);

        mainWindow = new BagAssistantWindow(this, DataManager);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Bag Assistant | /bagassistant",
        });
        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Bag Assistant | /ba",
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
        CommandManager.RemoveHandler(ShortCommand);
        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
    }
}
