using Dalamud.Bindings.ImGui;
using Dalamud.Game.NativeWrapper;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace Slidecaster.Windows;

public sealed unsafe class CastbarOverlayWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;

    private bool wasCasting;
    private uint trackedCastActionId;
    private DateTime castStartUtc;
    private float trackedCastDurationSeconds;
    private bool safeCuePlayed;
    private bool isCurrentlySafe;

    public CastbarOverlayWindow(
        Configuration configuration,
        IObjectTable objectTable,
        IGameGui gameGui,
        IPluginLog log)
        : base("##slidecaster_overlay",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoSavedSettings)
    {
        this.configuration = configuration;
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.log = log;

        IsOpen = false;
        RespectCloseHotkey = false;
    }

    public int GetConfiguredSafeWindowMs()
        => Math.Clamp(configuration.BaseSafeWindowMs + configuration.LatencyCompensationMs, 100, 1200);

    public void UpdateCastState()
    {
        var player = objectTable.LocalPlayer;
        var isCasting = player is { IsCasting: true } && player.TotalCastTime > 0.01f;

        if (!isCasting)
        {
            ResetCastTracking();
            return;
        }

        var castActionId = player!.CastActionId;
        var castDuration = MathF.Max(0.01f, player.TotalCastTime);

        if (!wasCasting || castActionId != trackedCastActionId || MathF.Abs(castDuration - trackedCastDurationSeconds) > 0.05f)
        {
            trackedCastActionId = castActionId;
            trackedCastDurationSeconds = castDuration;
            castStartUtc = DateTime.UtcNow;
            safeCuePlayed = false;
        }

        wasCasting = true;
        IsOpen = true;

        var elapsedSeconds = (float)(DateTime.UtcNow - castStartUtc).TotalSeconds;
        var remainingSeconds = MathF.Max(0f, trackedCastDurationSeconds - elapsedSeconds);
        var safeWindowSeconds = GetConfiguredSafeWindowMs() / 1000f;
        isCurrentlySafe = remainingSeconds <= safeWindowSeconds;

        if (isCurrentlySafe && !safeCuePlayed && configuration.PlaySafeMoveSound)
        {
            safeCuePlayed = true;
            PlaySafeCue();
        }
    }

    private void ResetCastTracking()
    {
        IsOpen = false;
        wasCasting = false;
        trackedCastActionId = 0;
        trackedCastDurationSeconds = 0f;
        safeCuePlayed = false;
        isCurrentlySafe = false;
    }

    private void PlaySafeCue()
    {
        try
        {
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Console.Beep(1200, 90);
                }
                catch
                {
                    // Beep support depends on host audio capabilities.
                }
            });
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Slidecaster failed to play sound cue.");
        }
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (!wasCasting || trackedCastDurationSeconds <= 0.01f)
            return;

        var drawList = ImGui.GetForegroundDrawList();

        if (!TryGetCastbarRect(out var castbarPos, out var castbarSize))
            return;

        var elapsedSeconds = (float)(DateTime.UtcNow - castStartUtc).TotalSeconds;
        var castProgress = Math.Clamp(elapsedSeconds / trackedCastDurationSeconds, 0f, 1f);
        var safeWindowRatio = Math.Clamp((GetConfiguredSafeWindowMs() / 1000f) / trackedCastDurationSeconds, 0f, 1f);
        var safeStartRatio = 1f - safeWindowRatio;

        var alpha = Math.Clamp(configuration.OverlayOpacity, 0.1f, 1f);

        var x1 = castbarPos.X;
        var y1 = castbarPos.Y;
        var x2 = castbarPos.X + castbarSize.X;
        var y2 = castbarPos.Y + castbarSize.Y;

        var safeX = x1 + castbarSize.X * safeStartRatio;
        var progressX = x1 + castbarSize.X * castProgress;

        var safeZoneColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.9f, 0.3f, alpha));
        var activeSafeZoneColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 1.0f, 0.35f, Math.Clamp(alpha + 0.2f, 0f, 1f)));
        var outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.0f, 0.0f, 0.7f));
        var markerColor = ImGui.ColorConvertFloat4ToU32(isCurrentlySafe
            ? new Vector4(0.0f, 1.0f, 0.2f, 0.95f)
            : new Vector4(1.0f, 0.75f, 0.1f, 0.95f));

        drawList.AddRectFilled(new Vector2(safeX, y1), new Vector2(x2, y2), isCurrentlySafe ? activeSafeZoneColor : safeZoneColor);
        drawList.AddRect(new Vector2(x1, y1), new Vector2(x2, y2), outlineColor, 0f, ImDrawFlags.None, 1.2f);

        drawList.AddLine(
            new Vector2(progressX, y1 - 2f),
            new Vector2(progressX, y2 + 2f),
            markerColor,
            2.4f);

        if (configuration.ShowSafeText)
        {
            var label = isCurrentlySafe ? "SAFE TO MOVE" : "CASTING";
            var textColor = ImGui.ColorConvertFloat4ToU32(isCurrentlySafe
                ? new Vector4(0.2f, 1f, 0.2f, 0.95f)
                : new Vector4(1f, 0.9f, 0.25f, 0.95f));
            var textSize = ImGui.CalcTextSize(label);
            var textPos = new Vector2((x1 + x2 - textSize.X) * 0.5f, y1 - textSize.Y - 4f);
            drawList.AddText(textPos, textColor, label);
        }
    }

    private bool TryGetCastbarRect(out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;

        AtkUnitBasePtr addon = gameGui.GetAddonByName("_CastBar", 1);
        if (addon.IsNull)
            return false;

        if (!addon.IsVisible)
            return false;

        var width = MathF.Max(120f, addon.ScaledWidth);
        var height = MathF.Max(12f, addon.ScaledHeight);

        position = addon.Position;
        size = new Vector2(width, height);
        return true;
    }
}
