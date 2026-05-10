using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using QuestNav.Services;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace QuestNav.Windows
{
    public sealed class QuestNavWindow : Window, IDisposable
    {
        private readonly QuestService questService;
        private readonly IClientState clientState;

        private List<QuestEntry> quests = [];
        private string statusMessage = string.Empty;

        public QuestNavWindow(QuestService questService, IClientState clientState)
            : base("QuestNav##window")
        {
            this.questService = questService;
            this.clientState = clientState;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(520, 200),
                MaximumSize = new Vector2(900, 700),
            };
            Size = new Vector2(580, 340);
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

        private void DrawToolbar()
        {
            if (ImGui.Button("Refresh##refresh"))
            {
                quests = questService.GetActiveQuests();
                statusMessage = quests.Count == 0
                    ? "No active quests found."
                    : $"Found {quests.Count} active quest(s).";
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"{quests.Count} quest(s) loaded");
        }

        private void DrawQuestTable()
        {
            var tableFlags = ImGuiTableFlags.Borders
                           | ImGuiTableFlags.RowBg
                           | ImGuiTableFlags.ScrollY
                           | ImGuiTableFlags.SizingStretchProp;

            // Reserve space for the status line and footnote
            var tableHeight = ImGui.GetContentRegionAvail().Y - (string.IsNullOrEmpty(statusMessage) ? 48f : 68f);
            if (tableHeight < 60f) tableHeight = 60f;

            if (!ImGui.BeginTable("##quests", 3, tableFlags, new Vector2(0, tableHeight)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Quest",           ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Nearest Aetheryte", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("##teleport",      ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableHeadersRow();

            if (quests.Count == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextDisabled("Click Refresh to load your active quests.");
            }

            foreach (var quest in quests)
            {
                ImGui.TableNextRow();

                // Quest name + zone sub-text
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(quest.Name);
                ImGui.TableSetColumnIndex(1);

                if (quest.NearestAetheryteId.HasValue)
                    ImGui.TextUnformatted(quest.AetheryteName);
                else
                    ImGui.TextDisabled(quest.AetheryteName);

                // Teleport button
                ImGui.TableSetColumnIndex(2);
                var canTeleport = quest.NearestAetheryteId.HasValue;
                if (!canTeleport)
                    ImGui.BeginDisabled();

                if (ImGui.Button($"Teleport##{quest.QuestId}"))
                {
                    var success = questService.Teleport(quest.NearestAetheryteId!.Value);
                    statusMessage = success
                        ? $"Teleporting to {quest.AetheryteName} for \"{quest.Name}\"..."
                        : "Teleport failed — make sure you are not in combat.";
                }

                if (!canTeleport)
                    ImGui.EndDisabled();
            }

            ImGui.EndTable();
        }

        private void DrawFootnote()
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Locations are based on the quest giver's position.");
        }
    }
}
