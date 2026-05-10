using Dalamud.Bindings.ImGui;
using Dalamud.Game.NativeWrapper;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

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
            var volume = Math.Clamp(configuration.SafeCueVolume, 0f, 1f);
            if (volume <= 0.001f)
                return;

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    PlayBeepTone(1200, 90, volume);
                }
                catch
                {
                    // Audio device may be unavailable; silently ignore.
                }
            });
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Slidecaster failed to play sound cue.");
        }
    }

    /// <summary>
    /// Plays the safe-move cue immediately at the configured volume, regardless of cast state.
    /// Used by the settings UI Test button.
    /// </summary>
    public void PlaySafeCuePreview() => PlaySafeCue();

    /// <summary>
    /// Genethe Win32 PlaySound API. Unlike Console.Beep this honors a volume setting because the
    /// PCM samples themselves are scaled.
    /// </summary>
    private static void PlayBeepTone(int frequencyHz, int durationMs, float volume)
    {
        const int sampleRate = 22050;
        const short bitsPerSample = 16;
        const short channels = 1;
        var sampleCount = sampleRate * durationMs / 1000;
        var dataBytes = sampleCount * 2;

        using var ms = new System.IO.MemoryStream(44 + dataBytes);
        using (var bw = new System.IO.BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            // RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataBytes);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            // fmt chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write(channels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * channels * bitsPerSample / 8);
            bw.Write((short)(channels * bitsPerSample / 8));
            bw.Write(bitsPerSample);
            // data chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataBytes);

            var amplitude = (short)(short.MaxValue * Math.Clamp(volume, 0f, 1f) * 0.6f);
            // Short fade in/out (5ms each) to avoid clicks.
            var fadeSamples = Math.Min(sampleCount / 4, sampleRate * 5 / 1000);
            for (var i = 0; i < sampleCount; i++)
            {
                var t = (double)i / sampleRate;
                var env = 1.0;
                if (i < fadeSamples) env = (double)i / fadeSamples;
                else if (i > sampleCount - fadeSamples) env = (double)(sampleCount - i) / fadeSamples;
                var sample = (short)(amplitude * env * Math.Sin(2 * Math.PI * frequencyHz * t));
                bw.Write(sample);
            }
        }

        var wavBytes = ms.ToArray();
        // SND_MEMORY | SND_ASYNC | SND_NODEFAULT — play raw WAV from memory without blocking.
        const uint flags = 0x0004u | 0x0001u | 0x0002u;
        PlaySound(wavBytes, IntPtr.Zero, flags);
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(byte[] pszSound, IntPtr hMod, uint fdwSound);

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
        var baseR = Math.Clamp(configuration.OverlayColorR, 0f, 1f);
        var baseG = Math.Clamp(configuration.OverlayColorG, 0f, 1f);
        var baseB = Math.Clamp(configuration.OverlayColorB, 0f, 1f);
        // Slightly brighter accent when the safe window is active.
        var activeR = Math.Clamp(baseR + 0.10f, 0f, 1f);
        var activeG = Math.Clamp(baseG + 0.10f, 0f, 1f);
        var activeB = Math.Clamp(baseB + 0.10f, 0f, 1f);

        var x1 = castbarPos.X;
        var x2 = castbarPos.X + castbarSize.X - configuration.OverlayEndTrimPx;
        if (x2 < x1 + 12f)
            x2 = x1 + 12f;

        var width = x2 - x1;
        var overlayHeight = castbarSize.Y * Math.Clamp(configuration.OverlayHeightScale, 0.5f, 3.0f);
        var overlayY1 = castbarPos.Y + (castbarSize.Y - overlayHeight) * 0.5f;
        var overlayY2 = overlayY1 + overlayHeight;

        var safeBarHeight = overlayHeight * Math.Clamp(configuration.SafeBarHeightScale, 0.2f, 3.0f);
        var y1 = overlayY1 + (overlayHeight - safeBarHeight) * 0.5f;
        var y2 = y1 + safeBarHeight;

        var safeX = x1 + width * safeStartRatio;
        var progressStartX = x1;
        if (configuration.EnableProgressMarkerStartOffset)
            progressStartX += configuration.ProgressMarkerStartOffsetPx;

        if (progressStartX > x2 - 6f)
            progressStartX = x2 - 6f;

        var progressTrackWidth = MathF.Max(6f, x2 - progressStartX);
        var progressX = progressStartX + progressTrackWidth * castProgress;

        var safeZoneColor = ImGui.ColorConvertFloat4ToU32(new Vector4(baseR, baseG, baseB, alpha));
        var activeSafeZoneColor = ImGui.ColorConvertFloat4ToU32(new Vector4(activeR, activeG, activeB, Math.Clamp(alpha + 0.2f, 0f, 1f)));
        var outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.0f, 0.0f, 0.7f));
        var markerColor = ImGui.ColorConvertFloat4ToU32(isCurrentlySafe
            ? new Vector4(activeR, activeG, activeB, 0.95f)
            : new Vector4(1.0f, 0.75f, 0.1f, 0.95f));

        drawList.AddRectFilled(new Vector2(safeX, y1), new Vector2(x2, y2), isCurrentlySafe ? activeSafeZoneColor : safeZoneColor);
        if (configuration.ShowCastBarBorder)
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
                ? new Vector4(activeR, activeG, activeB, 0.95f)
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
