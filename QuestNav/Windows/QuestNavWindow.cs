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
                MinimumSize = new Vector2(560, 200),
                MaximumSize = new Vector2(960, 800),
            };
            Size = new Vector2(700, 380);
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
            if (ImGui.Button("Refresh##refresh"))
            {
                Refresh();
                statusMessage = quests.Count == 0
                    ? "No active quests found."
                    : $"Found {quests.Count} active quest(s).";
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"{quests.Count} quest(s)");

            ImGui.SameLine();
            ImGui.Spacing();
            ImGui.SameLine();

            // Compact toggle
            bool compact = config.CompactMode;
            if (ImGui.Checkbox("Compact##compact", ref compact))
            {
                config.CompactMode = compact;
                config.Save();
            }
            ImGui.SameLine();

            // Arrow toggle
            bool arrow = config.ShowArrow;
            if (ImGui.Checkbox("Arrow##arrow", ref arrow))
            {
                config.ShowArrow = arrow;
                config.Save();
                SyncArrowTarget();
            }
            ImGui.SameLine();

            // Auto-refresh toggle
            bool autoR = config.AutoRefresh;
            if (ImGui.Checkbox("Auto-refresh##autorefresh", ref autoR))
            {
                config.AutoRefresh = autoR;
                config.Save();
            }
        }

        private void DrawQuestTable()
        {
            // Column layout differs between compact and normal mode
            var tableFlags = ImGuiTableFlags.Borders
                           | ImGuiTableFlags.RowBg
                           | ImGuiTableFlags.ScrollY
                           | ImGuiTableFlags.SizingStretchProp;

            float footerHeight = 48f + (string.IsNullOrEmpty(statusMessage) ? 0f : 20f);
            var tableHeight = ImGui.GetContentRegionAvail().Y - footerHeight;
            if (tableHeight < 60f) tableHeight = 60f;

            // 6 columns: Type | Quest | Zone/Aetheryte | Cost | Actions (Nav/Flag/Teleport)
            if (!ImGui.BeginTable("##quests", 6, tableFlags, new Vector2(0, tableHeight)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("##type",     ImGuiTableColumnFlags.WidthFixed,   44f);
            ImGui.TableSetupColumn("Quest",      ImGuiTableColumnFlags.WidthStretch, 2.2f);
            ImGui.TableSetupColumn("Zone",       ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableSetupColumn("Aetheryte",  ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableSetupColumn("Cost",       ImGuiTableColumnFlags.WidthFixed,   52f);
            ImGui.TableSetupColumn("##actions",  ImGuiTableColumnFlags.WidthFixed,   config.CompactMode ? 82f : 174f);
            ImGui.TableHeadersRow();

            if (quests.Count == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(1);
                ImGui.TextDisabled("Click Refresh to load your active quests.");
            }

            var currentTerr = (uint)clientState.TerritoryType;

            foreach (var quest in quests)
            {
                ImGui.TableNextRow();
                bool inZone = quest.TerritoryId == currentTerr;

                // ── Type badge ───────────────────────────────────────────────
                ImGui.TableSetColumnIndex(0);
                DrawTypeBadge(quest.QuestType);

                // ── Quest name ───────────────────────────────────────────────
                ImGui.TableSetColumnIndex(1);
                if (inZone)
                    ImGui.TextColored(new Vector4(0.4f, 1f, 0.55f, 1f), quest.Name);
                else
                    ImGui.TextUnformatted(quest.Name);

                if (!config.CompactMode)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($" (step {quest.Sequence})");
                }

                // ── Zone ─────────────────────────────────────────────────────
                ImGui.TableSetColumnIndex(2);
                if (inZone)
                    ImGui.TextColored(new Vector4(0.4f, 1f, 0.55f, 1f), quest.ZoneName);
                else
                    ImGui.TextDisabled(quest.ZoneName);

                // ── Aetheryte ────────────────────────────────────────────────
                ImGui.TableSetColumnIndex(3);
                if (quest.NearestAetheryteId.HasValue)
                    ImGui.TextUnformatted(quest.AetheryteName);
                else
                    ImGui.TextDisabled(quest.AetheryteName);

                // ── Cost ─────────────────────────────────────────────────────
                ImGui.TableSetColumnIndex(4);
                if (quest.NearestAetheryteId.HasValue)
                {
                    if (quest.GilCost == 0)
                        ImGui.TextColored(new Vector4(0.4f, 1f, 0.55f, 1f), "Free");
                    else
                        ImGui.TextDisabled($"{quest.GilCost:N0}g");
                }

                // ── Actions ──────────────────────────────────────────────────
                ImGui.TableSetColumnIndex(5);

                bool canTeleport = quest.NearestAetheryteId.HasValue;
                bool isNavTarget = config.NavQuestId == quest.QuestId;

                if (config.CompactMode)
                {
                    // Compact: single "Nav" toggle + "TP" button
                    if (isNavTarget)
                    {
                        if (ImGui.Button($"Nav ✓##{quest.QuestId}_nav"))
                        {
                            config.NavQuestId = 0;
                            config.Save();
                            arrowOverlay.SetNavTarget(null);
                        }
                    }
                    else
                    {
                        if (ImGui.Button($"Nav##{quest.QuestId}_nav"))
                        {
                            config.NavQuestId = quest.QuestId;
                            config.Save();
                            arrowOverlay.SetNavTarget(quest);
                        }
                    }
                    ImGui.SameLine();
                    if (!canTeleport) ImGui.BeginDisabled();
                    if (ImGui.Button($"TP##{quest.QuestId}"))
                        DoTeleport(quest);
                    if (!canTeleport) ImGui.EndDisabled();
                }
                else
                {
                    // Full mode: Nav | Flag | Teleport
                    if (isNavTarget)
                    {
                        if (ImGui.Button($"Nav ✓##{quest.QuestId}_nav"))
                        {
                            config.NavQuestId = 0;
                            config.Save();
                            arrowOverlay.SetNavTarget(null);
                        }
                    }
                    else
                    {
                        if (ImGui.Button($"Nav##{quest.QuestId}_nav"))
                        {
                            config.NavQuestId = quest.QuestId;
                            config.Save();
                            arrowOverlay.SetNavTarget(quest);
                        }
                    }
                    ImGui.SameLine();

                    // Map flag button (always available if we have a location)
                    bool hasLocation = quest.TerritoryId != 0 && quest.MapId != 0;
                    if (!hasLocation) ImGui.BeginDisabled();
                    if (ImGui.Button($"Flag##{quest.QuestId}_flag"))
                        DoSetMapFlag(quest);
                    if (!hasLocation) ImGui.EndDisabled();
                    ImGui.SameLine();

                    if (!canTeleport) ImGui.BeginDisabled();
                    if (ImGui.Button($"Teleport##{quest.QuestId}"))
                        DoTeleport(quest);
                    if (!canTeleport) ImGui.EndDisabled();
                }
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
            ImGui.Spacing();
            ImGui.TextDisabled("Locations use the quest giver's position.  Green = you're already in that zone.");
        }
    }
}

