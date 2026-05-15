using System;
using System.Linq;
using System.Numerics;
using CombatStatistics.Config;
using CombatStatistics.Models;
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
        ImGui.SetNextWindowPos(new Vector2(configuration.OverlayWindowX, configuration.OverlayWindowY), ImGuiCond.FirstUseEver);
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
        var topActors = encounter.TopDamageActors(rows).ToList();
        var localActor = tracker.TryGetLocalPlayerStats();
        var localActorKey = localActor?.Actor.ActorKey;
        if (localActor != null && !string.IsNullOrWhiteSpace(localActorKey) && topActors.All(actor => actor.Actor.ActorKey != localActorKey))
        {
            topActors.Add(localActor);
            topActors = topActors.OrderByDescending(actor => actor.DamageTotal).Take(rows).ToList();
            if (topActors.All(actor => actor.Actor.ActorKey != localActorKey))
            {
                topActors[^1] = localActor;
            }
        }

        var raidDamage = Math.Max(1, encounter.TotalRaidDamage);
        var encounterSeconds = encounter.DurationSeconds;
        var usePerMinute = configuration.OverlayUsePerMinuteMetrics;
        var damageLabel = usePerMinute ? "DPM" : "DPS";
        var healingLabel = usePerMinute ? "HPM" : "HPS";
        var accent = new Vector4(configuration.OverlayAccentR, configuration.OverlayAccentG, configuration.OverlayAccentB, 1f);

        ImGui.PushStyleColor(ImGuiCol.Text, accent);
        ImGui.TextUnformatted($"Top {damageLabel}/{healingLabel}");
        ImGui.PopStyleColor();

        ImGui.Separator();

        foreach (var actor in topActors)
        {
            var percent = actor.DamageTotal * 100d / raidDamage;
            var damageRate = usePerMinute ? actor.GetDamagePerMinute(encounterSeconds) : actor.GetDamagePerSecond(encounterSeconds);
            var healingRate = usePerMinute ? actor.GetHealingPerMinute(encounterSeconds) : actor.GetHealingPerSecond(encounterSeconds);
            ImGui.TextUnformatted(actor.Actor.DisplayName);
            ImGui.SameLine(240f);
            ImGui.TextUnformatted($"{damageRate:N0} {damageLabel}");
            ImGui.SameLine(360f);
            ImGui.TextUnformatted($"{healingRate:N0} {healingLabel}");
            ImGui.SameLine(490f);
            ImGui.TextDisabled($"{percent:N1}%");
        }

        var position = ImGui.GetWindowPos();
        if (!position.Equals(new Vector2(configuration.OverlayWindowX, configuration.OverlayWindowY)))
        {
            configuration.OverlayWindowX = position.X;
            configuration.OverlayWindowY = position.Y;
            configuration.Save();
        }
    }
}
