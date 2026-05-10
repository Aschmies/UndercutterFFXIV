using System;
using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using QuestNav.Services;
using QuestNav.Windows;
using System.Linq;

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
        private string lastQuestStateSignature = string.Empty;
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
            var currentStateSignature = BuildQuestStateSignature(currentQuests);
            bool questStateChanged = !string.Equals(currentStateSignature, lastQuestStateSignature, StringComparison.Ordinal);
            lastQuestCount = currentQuests.Count;
            lastQuestStateSignature = currentStateSignature;

            if ((questsChanged || questStateChanged) && (DateTime.UtcNow - lastEventRefresh).TotalMilliseconds > 500)
            {
                mainWindow.TriggerRefresh();
                lastEventRefresh = DateTime.UtcNow;
            }
        }

        private static string BuildQuestStateSignature(System.Collections.Generic.List<QuestEntry> quests)
        {
            if (quests.Count == 0)
                return string.Empty;

            // Track objective-relevant state including current step location so progression triggers arrow update.
            var parts = new List<string>();
            foreach (var q in quests.OrderBy(q => q.QuestId))
            {
                var stepPart = "";
                if (q.AllSteps != null && q.Sequence < q.AllSteps.Count)
                {
                    var currentStep = q.AllSteps[(int)q.Sequence];
                    if (currentStep != null)
                    {
                        stepPart = $":{currentStep.NpcWorldX?.ToString("F2") ?? ""}:{currentStep.NpcWorldZ?.ToString("F2") ?? ""}";
                    }
                }
                parts.Add($"{q.QuestId}:{q.Sequence}:{q.TerritoryId}:{q.WorldX:F2}:{q.WorldZ:F2}{stepPart}");
            }
            return string.Join("|", parts);
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

