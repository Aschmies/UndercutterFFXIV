using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using CombatStatistics.Config;
using CombatStatistics.Models;
using CombatStatistics.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CombatStatistics.UI;

public sealed class CombatStatisticsWindow : Window
{
    private static readonly string DisplayVersion =
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "1.0.0";

    private readonly Configuration configuration;
    private readonly CombatStatisticsTracker tracker;
    private string selectedActorKey = string.Empty;

    public CombatStatisticsWindow(Configuration configuration, CombatStatisticsTracker tracker)
        : base($"Combat Statistics v{DisplayVersion}##mainWindow")
    {
        this.configuration = configuration;
        this.tracker = tracker;
        Size = new Vector2(configuration.MainWindowWidth, configuration.MainWindowHeight);
        SizeCondition = ImGuiCond.FirstUseEver;
        Position = new Vector2(configuration.MainWindowX, configuration.MainWindowY);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("##combatstats-tabs"))
        {
            if (ImGui.BeginTabItem("Summary"))
            {
                DrawSummaryTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("History"))
            {
                DrawHistoryTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawSummaryTab()
    {
        var encounter = tracker.CurrentEncounter;
        if (!tracker.HasActiveEncounter)
        {
            ImGui.TextWrapped("No active encounter. Combat Statistics will begin tracking when a party member deals or receives combat actions.");
            return;
        }

        ImGui.TextUnformatted($"Encounter started: {encounter.StartedUtc:HH:mm:ss} UTC");
        ImGui.TextUnformatted($"Events: {encounter.EventCount}");
        ImGui.TextUnformatted($"Raid damage: {encounter.TotalRaidDamage:N0}");
        ImGui.TextUnformatted($"Raid healing: {encounter.TotalRaidHealing:N0}");
        ImGui.Separator();

        if (ImGui.BeginTable("##combatstats-table", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable, new Vector2(0, 260f)))
        {
            ImGui.TableSetupColumn("Actor");
            ImGui.TableSetupColumn("Type");
            ImGui.TableSetupColumn("DPS");
            ImGui.TableSetupColumn("Damage");
            ImGui.TableSetupColumn("HPS");
            ImGui.TableSetupColumn("Healing");
            ImGui.TableSetupColumn("Overheal%");
            ImGui.TableHeadersRow();

            foreach (var actor in encounter.Actors.Values.OrderByDescending(x => x.DamageTotal))
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                if (ImGui.Selectable(actor.Actor.DisplayName, selectedActorKey == actor.Actor.ActorKey, ImGuiSelectableFlags.SpanAllColumns))
                    selectedActorKey = actor.Actor.ActorKey;

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(actor.Actor.Type.ToString());

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{actor.Dps:N0}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{actor.DamageTotal:N0}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{actor.Hps:N0}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{actor.HealingTotal:N0}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{actor.OverhealPercent:N1}%");
            }

            ImGui.EndTable();
        }

        ImGui.Separator();
        DrawActorDetails(encounter);
    }

    private void DrawActorDetails(EncounterSummary encounter)
    {
        if (string.IsNullOrWhiteSpace(selectedActorKey) || !encounter.Actors.TryGetValue(selectedActorKey, out var actor))
        {
            ImGui.TextDisabled("Select a row to view per-action damage and healing breakdown.");
            return;
        }

        ImGui.TextUnformatted(actor.Actor.DisplayName);
        ImGui.TextDisabled($"Active time: {actor.ActiveTimeSeconds:N1}s | Pet damage: {actor.PetDamageTotal:N0} | Shields: {actor.ShieldTotal:N0}");
        ImGui.Separator();

        if (ImGui.BeginTable("##combatstats-detail", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Action ID");
            ImGui.TableSetupColumn("Damage");
            ImGui.TableSetupColumn("Healing");
            ImGui.TableHeadersRow();

            foreach (var pair in actor.TopDamageActions(8))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"0x{pair.Key:X}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{pair.Value:N0}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.Empty);
            }

            foreach (var pair in actor.TopHealingActions(8))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"0x{pair.Key:X}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.Empty);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{pair.Value:N0}");
            }

            ImGui.EndTable();
        }
    }

    private void DrawSettingsTab()
    {
        var mergePets = configuration.MergePetsWithOwner;
        if (ImGui.Checkbox("Merge pet damage with owner", ref mergePets))
        {
            configuration.MergePetsWithOwner = mergePets;
            configuration.Save();
        }

        var showOverlay = configuration.ShowOverlay;
        if (ImGui.Checkbox("Show overlay", ref showOverlay))
        {
            configuration.ShowOverlay = showOverlay;
            configuration.Save();
        }

        var timeout = configuration.EncounterTimeoutSeconds;
        if (ImGui.SliderInt("Encounter timeout (seconds)", ref timeout, 3, 30))
        {
            configuration.EncounterTimeoutSeconds = timeout;
            configuration.Save();
        }

        var overlayRows = configuration.OverlayRowCount;
        if (ImGui.SliderInt("Overlay rows", ref overlayRows, 1, 12))
        {
            configuration.OverlayRowCount = overlayRows;
            configuration.Save();
        }

        var overlayOpacity = configuration.OverlayOpacity;
        if (ImGui.SliderFloat("Overlay opacity", ref overlayOpacity, 0.2f, 1f, "%.2f"))
        {
            configuration.OverlayOpacity = overlayOpacity;
            configuration.Save();
        }

        var fontSize = configuration.OverlayFontSize;
        if (ImGui.SliderFloat("Overlay font size", ref fontSize, 10f, 22f, "%.1f"))
        {
            configuration.OverlayFontSize = fontSize;
            configuration.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Overlay colors");
        var accent = new Vector3(configuration.OverlayAccentR, configuration.OverlayAccentG, configuration.OverlayAccentB);
        if (ImGui.ColorEdit3("Accent", ref accent))
        {
            configuration.OverlayAccentR = accent.X;
            configuration.OverlayAccentG = accent.Y;
            configuration.OverlayAccentB = accent.Z;
            configuration.Save();
        }

        if (ImGui.Button("Save Current as Defaults"))
        {
            configuration.CaptureCurrentAsDefaults();
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset to Saved Defaults"))
        {
            configuration.ApplySavedDefaults();
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset to Factory Defaults"))
        {
            configuration.ResetToFactoryDefaults();
        }
    }

    private void DrawHistoryTab()
    {
        if (tracker.History.Count == 0)
        {
            ImGui.TextDisabled("No archived encounters yet.");
            return;
        }

        foreach (var entry in tracker.History.Take(10))
        {
            ImGui.TextUnformatted($"{entry.StartedUtc:HH:mm:ss} - {entry.EndedUtc:HH:mm:ss} UTC");
            ImGui.TextDisabled($"Duration {entry.Duration.TotalSeconds:N1}s | Events {entry.EventCount} | Raid Damage {entry.TotalRaidDamage:N0}");
            ImGui.Separator();
        }
    }
}
