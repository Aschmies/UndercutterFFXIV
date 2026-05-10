using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using QuestNav.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace QuestNav.Windows
{
    public sealed class QuestNavWindow : Window, IDisposable
    {
        private readonly QuestService questService;
        private readonly IClientState clientState;
        private readonly IGameGui gameGui;
        private readonly Configuration config;
        private readonly ArrowOverlayWindow arrowOverlay;

        private List<QuestEntry> quests = [];
        private string statusMessage = string.Empty;
        private DateTime lastRefresh = DateTime.MinValue;
        private const float AutoRefreshSeconds = 30f;
        private bool showSettings = false;

        // Maps JournalGenre name keywords → badge label + colour
        private static readonly (string Keyword, string Badge, Vector4 Color)[] TypeRules =
        [
            ("Main",  "MSQ",   new Vector4(1.00f, 0.85f, 0.10f, 1f)),
            ("Class", "Class", new Vector4(0.40f, 0.90f, 0.55f, 1f)),
            ("Job",   "Job",   new Vector4(0.40f, 0.90f, 0.55f, 1f)),
            ("Side",  "Side",  new Vector4(0.80f, 0.80f, 0.80f, 1f)),
            ("Clan",  "Clan",  new Vector4(0.70f, 0.55f, 1.00f, 1f)),
            ("Grand", "GC",    new Vector4(0.65f, 0.85f, 1.00f, 1f)),
        ];

        public QuestNavWindow(
            QuestService questService,
            IClientState clientState,
            IGameGui gameGui,
            Configuration config,
            ArrowOverlayWindow arrowOverlay)
            : base("QuestNav##window")
        {
            this.questService   = questService;
            this.clientState    = clientState;
            this.gameGui        = gameGui;
            this.config         = config;
            this.arrowOverlay   = arrowOverlay;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(400, 200),
                MaximumSize = new Vector2(1000, 900),
            };
            Size = new Vector2(550, 300);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public void Dispose() { }

        public override void Draw()
        {
            if (!clientState.IsLoggedIn)
            {
                ImGui.TextDisabled("Log in to a character to view active quests.");
                return;
            }

            // Auto-refresh
            if (config.AutoRefresh && (DateTime.UtcNow - lastRefresh).TotalSeconds > AutoRefreshSeconds)
                Refresh();

            DrawToolbar();
            ImGui.Separator();
            DrawQuestTable();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.6f, 1f, 0.6f, 1f), statusMessage);
            }

            DrawFootnote();
        }

        private void Refresh()
        {
            quests = questService.GetActiveQuests();
            lastRefresh = DateTime.UtcNow;

            // Sort: current zone first, then alphabetically
            var currentTerr = (uint)clientState.TerritoryType;
            quests = [.. quests.OrderByDescending(q => q.TerritoryId == currentTerr)
                               .ThenBy(q => q.Name)];

            // Update arrow target if a nav quest is selected
            SyncArrowTarget();

            if (statusMessage.StartsWith("Auto"))  // don't clobber user-triggered messages
                statusMessage = string.Empty;
        }

        private void SyncArrowTarget()
        {
            if (config.NavQuestId == 0)
            {
                arrowOverlay.SetNavTarget(null);
                return;
            }
            var target = quests.FirstOrDefault(q => q.QuestId == config.NavQuestId);
            arrowOverlay.SetNavTarget(target);
            if (target == null) config.NavQuestId = 0;  // quest finished/abandoned
        }

        private void DrawToolbar()
        {
            if (ImGui.Button("Refresh##refresh", new Vector2(70, 0)))
            {
                Refresh();
                statusMessage = quests.Count == 0
                    ? "No active quests."
                    : $"Loaded {quests.Count} quest(s).";
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"({quests.Count})");

            ImGui.SameLine();
            float availWidth = ImGui.GetContentRegionAvail().X - 80;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availWidth);

            // Settings gear button (collapsible)
            if (ImGui.SmallButton("⚙##settings"))
                showSettings = !showSettings;

            if (showSettings)
            {
                ImGui.Spacing();
                bool arrow = config.ShowArrow;
                if (ImGui.Checkbox("Arrow overlay##arrow", ref arrow))
                {
                    config.ShowArrow = arrow;
                    config.Save();
                    SyncArrowTarget();
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                float bgOpacity = config.ArrowBgOpacity;
                if (ImGui.SliderFloat("opacity##arrowbg", ref bgOpacity, 0f, 1f, "%.0f%%"))
                {
                    config.ArrowBgOpacity = bgOpacity;
                    config.Save();
                }

                bool autoR = config.AutoRefresh;
                if (ImGui.Checkbox("Auto-refresh##autorefresh", ref autoR))
                {
                    config.AutoRefresh = autoR;
                    config.Save();
                }

                bool compact = config.CompactMode;
                if (ImGui.Checkbox("Compact mode##compact", ref compact))
                {
                    config.CompactMode = compact;
                    config.Save();
                }
            }
        }

        private void DrawQuestTable()
        {
            var tableFlags = ImGuiTableFlags.Borders
                           | ImGuiTableFlags.RowBg
                           | ImGuiTableFlags.ScrollY
                           | ImGuiTableFlags.NoHostExtendX;

            float footerHeight = 30f + (string.IsNullOrEmpty(statusMessage) ? 0f : 20f);
            var tableHeight = ImGui.GetContentRegionAvail().Y - footerHeight;
            if (tableHeight < 40f) tableHeight = 40f;

            // 2 columns: Quest Info | Actions
            if (!ImGui.BeginTable("##quests", 2, tableFlags, new Vector2(0, tableHeight)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Quest",     ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, config.CompactMode ? 80f : 120f);
            ImGui.TableHeadersRow();

            if (quests.Count == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextDisabled("Click Refresh to load active quests.");
            }

            var currentTerr = (uint)clientState.TerritoryType;

            foreach (var quest in quests)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 40f);
                bool inZone = quest.TerritoryId == currentTerr;

                // ── Quest Info ────────────────────────────────────────────────
                ImGui.TableSetColumnIndex(0);

                // Quest name with zone highlighting
                if (inZone)
                    ImGui.TextColored(new Vector4(0.3f, 1f, 0.4f, 1f), quest.Name);
                else
                    ImGui.TextUnformatted(quest.Name);

                // Zone and aetheryte info on next line (smaller, dimmer)
                ImGui.TextDisabled($"{quest.ZoneName}");
                ImGui.SameLine();
                ImGui.TextDisabled("·");
                ImGui.SameLine();
                if (quest.NearestAetheryteId.HasValue)
                {
                    ImGui.TextDisabled($"{quest.AetheryteName}");
                    ImGui.SameLine();
                    ImGui.TextDisabled(quest.GilCost == 0 ? "(Free)" : $"({quest.GilCost:N0}g)");
                }
                else
                    ImGui.TextDisabled("No aetheryte");

                // Hover tooltip with more details
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), quest.Name);
                    if (!config.CompactMode)
                        ImGui.TextDisabled($"Step: {quest.Sequence}");
                    ImGui.TextDisabled($"Zone: {quest.ZoneName}");
                    if (quest.NearestAetheryteId.HasValue)
                        ImGui.TextDisabled($"Nearest: {quest.AetheryteName}");
                    ImGui.EndTooltip();
                }

                // ── Actions ───────────────────────────────────────────────────
                ImGui.TableSetColumnIndex(1);

                bool canTeleport = quest.NearestAetheryteId.HasValue;
                bool isNavTarget = config.NavQuestId == quest.QuestId;

                // Nav button
                var navLabel = isNavTarget ? "✓ Nav" : "Nav";
                if (ImGui.Button($"{navLabel}##{quest.QuestId}_nav", new Vector2(config.CompactMode ? 35 : 50, 0)))
                {
                    if (isNavTarget)
                    {
                        config.NavQuestId = 0;
                        arrowOverlay.SetNavTarget(null);
                    }
                    else
                    {
                        config.NavQuestId = quest.QuestId;
                        arrowOverlay.SetNavTarget(quest);
                    }
                    config.Save();
                }

                if (!config.CompactMode)
                {
                    ImGui.SameLine();
                    // Map flag button
                    bool hasLocation = quest.TerritoryId != 0 && quest.MapId != 0;
                    if (!hasLocation) ImGui.BeginDisabled();
                    if (ImGui.Button($"Flag##{quest.QuestId}_flag", new Vector2(50, 0)))
                        DoSetMapFlag(quest);
                    if (!hasLocation) ImGui.EndDisabled();
                }

                // Teleport button
                ImGui.SameLine();
                if (!canTeleport) ImGui.BeginDisabled();
                if (ImGui.Button($"TP##{quest.QuestId}", new Vector2(config.CompactMode ? 35 : 50, 0)))
                    DoTeleport(quest);
                if (!canTeleport) ImGui.EndDisabled();
            }

            ImGui.EndTable();
        }

        private void DoTeleport(QuestEntry quest)
        {
            var success = questService.Teleport(quest.NearestAetheryteId!.Value);
            statusMessage = success
                ? $"Teleporting to {quest.AetheryteName} for \"{quest.Name}\"..."
                : "Teleport failed — make sure you are not in combat.";
        }

        private void DoSetMapFlag(QuestEntry quest)
        {
            try
            {
                var payload = new MapLinkPayload(quest.TerritoryId, quest.MapId, quest.MapX, quest.MapY);
                gameGui.OpenMapWithMapLink(payload);
                statusMessage = $"Map flag set in {quest.ZoneName}.";
            }
            catch (Exception ex)
            {
                statusMessage = $"Could not set map flag: {ex.Message}";
            }
        }

        private static void DrawTypeBadge(string questType)
        {
            if (string.IsNullOrEmpty(questType)) return;

            var badge = "?";
            var color = new Vector4(0.6f, 0.6f, 0.6f, 1f);

            foreach (var (kw, lbl, col) in TypeRules)
            {
                if (questType.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    badge = lbl;
                    color = col;
                    break;
                }
            }

            ImGui.TextColored(color, badge);
        }

        private void DrawFootnote()
        {
            ImGui.TextDisabled("Green text = you're in that zone. Hover for details.");
        }
    }
}

