using System;
using System.Numerics;
using CombatStatistics.Config;
using CombatStatistics.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CombatStatistics.UI;

public sealed class CombatStatisticsOverlayWindow : Window
{
    private readonly Configuration configuration;
    private readonly CombatStatisticsTracker tracker;

    public CombatStatisticsOverlayWindow(Configuration configuration, CombatStatisticsTracker tracker)
        : base("##CombatStatisticsOverlay",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.configuration = configuration;
        this.tracker = tracker;
        IsOpen = configuration.ShowOverlay;
        RespectCloseHotkey = false;
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowPos(new Vector2(configuration.OverlayWindowX, configuration.OverlayWindowY), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(Math.Clamp(configuration.OverlayOpacity, 0.1f, 1f));
    }

    public override void Draw()
    {
        var encounter = tracker.CurrentEncounter;
        if (!tracker.HasActiveEncounter)
        {
            ImGui.TextDisabled("No active encounter");
            return;
        }

        var rows = Math.Max(1, configuration.OverlayRowCount);
        var topActors = encounter.TopDamageActors(rows);
        var raidDamage = Math.Max(1, encounter.TotalRaidDamage);
        var accent = new Vector4(configuration.OverlayAccentR, configuration.OverlayAccentG, configuration.OverlayAccentB, 1f);

        ImGui.PushStyleColor(ImGuiCol.Text, accent);
        ImGui.TextUnformatted("Top DPS");
        ImGui.PopStyleColor();

        ImGui.Separator();

        foreach (var actor in topActors)
        {
            var percent = actor.DamageTotal * 100d / raidDamage;
            ImGui.TextUnformatted(actor.Actor.DisplayName);
            ImGui.SameLine(220f);
            ImGui.TextUnformatted($"{actor.Dps:N0} DPS");
            ImGui.SameLine(320f);
            ImGui.TextDisabled($"{percent:N1}%");
        }
    }
}
