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
        private int activeFlagQuestId = 0;

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
                MinimumSize = new Vector2(280, 110),
                MaximumSize = new Vector2(900, 800),
            };
            Size = new Vector2(360, 160);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public void Dispose() { }

        public void TriggerRefresh()
        {
            Refresh();
        }

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
            if (!showSettings)
                ImGui.Spacing();
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
            float availWidth = ImGui.GetContentRegionAvail().X - 35;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availWidth);

            // Settings gear button (collapsible)
            if (ImGui.SmallButton("⚙##settings"))
                showSettings = !showSettings;

            if (showSettings)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 4f));
                
                bool arrow = config.ShowArrow;
                if (ImGui.Checkbox("Arrow##arrow", ref arrow))
                {
                    config.ShowArrow = arrow;
                    config.Save();
                    SyncArrowTarget();
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                float bgOpacity = config.ArrowBgOpacity;
                if (ImGui.SliderFloat("##opacity", ref bgOpacity, 0f, 1f, "%.0f%%"))
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
                if (ImGui.Checkbox("Compact##compact", ref compact))
                {
                    config.CompactMode = compact;
                    config.Save();
                }
                
                ImGui.PopStyleVar(2); // FramePadding, ItemSpacing
            }
        }

        private void DrawQuestTable()
        {
            var tableFlags = ImGuiTableFlags.Borders
                           | ImGuiTableFlags.RowBg
                           | ImGuiTableFlags.ScrollY
                           | ImGuiTableFlags.NoHostExtendX
                           | ImGuiTableFlags.SizingFixedFit;

            float footerHeight = 22f + (string.IsNullOrEmpty(statusMessage) ? 0f : 18f);
            var tableHeight = ImGui.GetContentRegionAvail().Y - footerHeight;
            if (tableHeight < 40f) tableHeight = 40f;

            // 2 columns: Quest Info | Actions
            if (!ImGui.BeginTable("##quests", 2, tableFlags, new Vector2(0, tableHeight)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Quest",     ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, config.CompactMode ? 78f : 138f);
            ImGui.TableHeadersRow();

            if (quests.Count == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextDisabled("Click Refresh to load active quests.");
            }

            var currentTerr = (uint)clientState.TerritoryType;
            var textLineHeight = ImGui.GetTextLineHeight();
            
            foreach (var quest in quests)
            {
                // More compact row - just one line of content
                ImGui.TableNextRow(ImGuiTableRowFlags.None, textLineHeight + 4f);
                bool inZone = quest.TerritoryId == currentTerr;

                // ── Quest Info (condensed to one line) ────────────────────────
                ImGui.TableSetColumnIndex(0);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() - 8f);

                // Quest name with zone highlighting
                if (inZone)
                    ImGui.TextColored(new Vector4(0.3f, 1f, 0.4f, 1f), quest.Name);
                else
                    ImGui.TextUnformatted(quest.Name);

                // Zone/aetheryte on same line, small text
                ImGui.SameLine();
                ImGui.TextDisabled(" · ");
                ImGui.SameLine();
                ImGui.TextDisabled(quest.ZoneName);

                if (quest.NearestAetheryteId.HasValue)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($" ({(quest.GilCost == 0 ? "Free" : $"{quest.GilCost}g")})");
                }

                ImGui.PopTextWrapPos();

                // Hover tooltip with full details
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), quest.Name);
                    ImGui.TextDisabled($"Zone: {quest.ZoneName}");
                    if (!config.CompactMode)
                        ImGui.TextDisabled($"Step: {quest.Sequence}");
                    if (quest.NearestAetheryteId.HasValue)
                        ImGui.TextDisabled($"Teleport: {quest.AetheryteName} ({(quest.GilCost == 0 ? "Free" : $"{quest.GilCost}g")})");
                    ImGui.EndTooltip();
                }

                // ── Actions ───────────────────────────────────────────────────
                ImGui.TableSetColumnIndex(1);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3f, 0f)); // Reduce button spacing

                bool canTeleport = quest.NearestAetheryteId.HasValue;
                bool isNavTarget = config.NavQuestId == quest.QuestId;

                // Nav button
                var navLabel = isNavTarget ? "✓" : "Nav";
                var navWidth = isNavTarget ? 28f : (config.CompactMode ? 36f : 44f);
                if (ImGui.Button($"{navLabel}##{quest.QuestId}_nav", new Vector2(navWidth, 0)))
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
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(isNavTarget
                        ? "Clear arrow navigation for this quest"
                        : "Show arrow overlay pointing to this quest");

                if (!config.CompactMode)
                {
                    ImGui.SameLine();
                    // Map flag button — toggles between set and clear
                    bool hasLocation = quest.TerritoryId != 0 && quest.MapId != 0;
                    bool isFlagged   = activeFlagQuestId == quest.QuestId;
                    if (!hasLocation) ImGui.BeginDisabled();
                    var flagLabel = isFlagged ? "Unflag" : "Flag";
                    if (ImGui.Button($"{flagLabel}##{quest.QuestId}_flag", new Vector2(50f, 0)))
                    {
                        if (isFlagged)
                            ClearMapFlag();
                        else
                            DoSetMapFlag(quest);
                    }
                    if (!hasLocation) ImGui.EndDisabled();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(isFlagged
                            ? "Clear the map flag for this quest"
                            : "Place a map flag at this quest's location");
                }

                // Teleport button
                ImGui.SameLine();
                if (!canTeleport) ImGui.BeginDisabled();
                if (ImGui.Button($"TP##{quest.QuestId}", new Vector2(config.CompactMode ? 36f : 44f, 0)))
                    DoTeleport(quest);
                if (!canTeleport) ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    if (canTeleport)
                        ImGui.SetTooltip($"Teleport to {quest.AetheryteName}\n({(quest.GilCost == 0 ? "Free" : $"{quest.GilCost}g")})");
                    else
                        ImGui.SetTooltip("No aetheryte near this quest");
                }

                ImGui.PopStyleVar(); // ItemSpacing
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
                activeFlagQuestId = quest.QuestId;
                statusMessage = $"Map flag set in {quest.ZoneName}.";
            }
            catch (Exception ex)
            {
                statusMessage = $"Could not set map flag: {ex.Message}";
            }
        }

        private void ClearMapFlag()
        {
            activeFlagQuestId = 0;
            statusMessage = "Map flag cleared.";
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
            ImGui.TextDisabled("Green = current zone");
        }
    }
}

