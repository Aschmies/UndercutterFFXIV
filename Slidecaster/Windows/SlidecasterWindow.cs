using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Reflection;
using System.Numerics;

namespace Slidecaster.Windows;

public sealed class SlidecasterWindow : Window
{
    private static readonly string DisplayVersion =
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "1.0.0";

    private readonly Configuration configuration;
    private readonly CastbarOverlayWindow overlayWindow;
    private string statusMessage = string.Empty;

    public SlidecasterWindow(Configuration configuration, CastbarOverlayWindow overlayWindow)
        : base($"Slidecaster v{DisplayVersion} Settings##window")
    {
        this.configuration = configuration;
        this.overlayWindow = overlayWindow;

        Size = new Vector2(460f, 320f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Slidecasting in FFXIV typically becomes reliable near the final 0.5s of a cast, with exact timing influenced by latency.");
        ImGui.Spacing();

        var baseWindow = configuration.BaseSafeWindowMs;
        if (ImGui.SliderInt("Base Safe Window (ms)", ref baseWindow, 200, 900))
        {
            configuration.BaseSafeWindowMs = baseWindow;
            configuration.Save();
        }

        var latencyComp = configuration.LatencyCompensationMs;
        if (ImGui.SliderInt("Latency Compensation (ms)", ref latencyComp, 0, 300))
        {
            configuration.LatencyCompensationMs = latencyComp;
            configuration.Save();
        }


        var opacity = configuration.OverlayOpacity;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderFloat("Overlay Opacity", ref opacity, 0.15f, 0.90f, "%.2f"))
        {
            configuration.OverlayOpacity = opacity;
            configuration.Save();
        }
        ImGui.SameLine();
        var opacityExact = configuration.OverlayOpacity;
        ImGui.SetNextItemWidth(70f);
        if (ImGui.InputFloat("##overlay-opacity-exact", ref opacityExact, 0.01f, 0.05f, "%.2f"))
        {
            configuration.OverlayOpacity = Math.Clamp(opacityExact, 0.15f, 0.90f);
            configuration.Save();
        }

        var color = new Vector3(configuration.OverlayColorR, configuration.OverlayColorG, configuration.OverlayColorB);
        if (ImGui.ColorEdit3("Overlay Color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha))
        {
            configuration.OverlayColorR = color.X;
            configuration.OverlayColorG = color.Y;
            configuration.OverlayColorB = color.Z;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset Color##resetColor"))
        {
            configuration.OverlayColorR = 0.10f;
            configuration.OverlayColorG = 0.90f;
            configuration.OverlayColorB = 0.30f;
            configuration.Save();
        }

        var overlayHeightScale = configuration.OverlayHeightScale;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderFloat("Overlay Height Scale", ref overlayHeightScale, 0.5f, 2.5f, "%.2f"))
        {
            configuration.OverlayHeightScale = overlayHeightScale;
            configuration.Save();
        }
        ImGui.SameLine();
        var overlayHeightExact = configuration.OverlayHeightScale;
        ImGui.SetNextItemWidth(70f);
        if (ImGui.InputFloat("##overlay-height-exact", ref overlayHeightExact, 0.05f, 0.20f, "%.2f"))
        {
            configuration.OverlayHeightScale = Math.Clamp(overlayHeightExact, 0.5f, 2.5f);
            configuration.Save();
        }

        var safeBarHeightScale = configuration.SafeBarHeightScale;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderFloat("Safe Bar Height Scale", ref safeBarHeightScale, 0.3f, 2.5f, "%.2f"))
        {
            configuration.SafeBarHeightScale = safeBarHeightScale;
            configuration.Save();
        }
        ImGui.SameLine();
        var safeBarHeightExact = configuration.SafeBarHeightScale;
        ImGui.SetNextItemWidth(70f);
        if (ImGui.InputFloat("##safe-bar-height-exact", ref safeBarHeightExact, 0.05f, 0.20f, "%.2f"))
        {
            configuration.SafeBarHeightScale = Math.Clamp(safeBarHeightExact, 0.3f, 2.5f);
            configuration.Save();
        }

        var showBorder = configuration.ShowCastBarBorder;
        if (ImGui.Checkbox("Show cast bar frame border", ref showBorder))
        {
            configuration.ShowCastBarBorder = showBorder;
            configuration.Save();
        }

        var endTrim = configuration.OverlayEndTrimPx;
        if (ImGui.SliderFloat("Overlay End Trim (px)", ref endTrim, -20f, 120f, "%.1f"))
        {
            configuration.OverlayEndTrimPx = endTrim;
            configuration.Save();
        }
        ImGui.SameLine();
        var endTrimExact = configuration.OverlayEndTrimPx;
        ImGui.SetNextItemWidth(90f);
        if (ImGui.InputFloat("##overlay-end-trim-exact", ref endTrimExact, 1f, 5f, "%.1f"))
        {
            configuration.OverlayEndTrimPx = Math.Clamp(endTrimExact, -20f, 120f);
            configuration.Save();
        }

        var yOffset = configuration.OverlayYOffsetPx;
        if (ImGui.SliderFloat("Overlay Y Offset (px)", ref yOffset, -40f, 40f, "%.1f"))
        {
            configuration.OverlayYOffsetPx = yOffset;
            configuration.Save();
        }
        ImGui.SameLine();
        var yOffsetExact = configuration.OverlayYOffsetPx;
        ImGui.SetNextItemWidth(90f);
        if (ImGui.InputFloat("##overlay-y-offset-exact", ref yOffsetExact, 1f, 5f, "%.1f"))
        {
            configuration.OverlayYOffsetPx = Math.Clamp(yOffsetExact, -40f, 40f);
            configuration.Save();
        }

        var enableMarkerStartOffset = configuration.EnableProgressMarkerStartOffset;
        if (ImGui.Checkbox("Adjust moving bar start", ref enableMarkerStartOffset))
        {
            configuration.EnableProgressMarkerStartOffset = enableMarkerStartOffset;
            configuration.Save();
        }

        if (configuration.EnableProgressMarkerStartOffset)
        {
            var markerStartOffset = configuration.ProgressMarkerStartOffsetPx;
            if (ImGui.SliderFloat("Moving Bar Start Offset (px)", ref markerStartOffset, -40f, 160f, "%.1f"))
            {
                configuration.ProgressMarkerStartOffsetPx = markerStartOffset;
                configuration.Save();
            }
            ImGui.SameLine();
            var markerStartOffsetExact = configuration.ProgressMarkerStartOffsetPx;
            ImGui.SetNextItemWidth(90f);
            if (ImGui.InputFloat("##moving-bar-start-offset-exact", ref markerStartOffsetExact, 1f, 5f, "%.1f"))
            {
                configuration.ProgressMarkerStartOffsetPx = Math.Clamp(markerStartOffsetExact, -40f, 160f);
                configuration.Save();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Styling Settings");
        ImGui.Spacing();

        var roundRightSide = configuration.RoundRightSide;
        if (ImGui.Checkbox("Round right side of shape", ref roundRightSide))
        {
            configuration.RoundRightSide = roundRightSide;
            configuration.Save();
        }

        var drawAsLine = configuration.DrawAsLine;
        if (ImGui.Checkbox("Draw as line (instead of block)", ref drawAsLine))
        {
            configuration.DrawAsLine = drawAsLine;
            configuration.Save();
        }

        if (configuration.DrawAsLine)
        {
            var lineThickness = configuration.LineThickness;
            if (ImGui.SliderFloat("Line Thickness", ref lineThickness, 1f, 10f, "%.1f"))
            {
                configuration.LineThickness = lineThickness;
                configuration.Save();
            }
            var lineHeightScale = configuration.LineHeightScale;
            if (ImGui.SliderFloat("Line Height Scale", ref lineHeightScale, 0.5f, 3.0f, "%.2f"))
            {
                configuration.LineHeightScale = lineHeightScale;
                configuration.Save();
            }
        }

        var showSafeText = configuration.ShowSafeText;
        if (ImGui.Checkbox("Show SAFE TO MOVE text", ref showSafeText))
        {
            configuration.ShowSafeText = showSafeText;
            configuration.Save();
        }

        var playCue = configuration.PlaySafeMoveSound;
        if (ImGui.Checkbox("Play sound cue when safe", ref playCue))
        {
            configuration.PlaySafeMoveSound = playCue;
            configuration.Save();
        }

        var cueVolume = configuration.SafeCueVolume;
        if (ImGui.SliderFloat("Sound Cue Volume", ref cueVolume, 0f, 1f, "%.2f"))
        {
            configuration.SafeCueVolume = Math.Clamp(cueVolume, 0f, 1f);
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Test##testCue"))
            overlayWindow.PlaySafeCuePreview();

        ImGui.Spacing();
        if (ImGui.Button("Save Current as Defaults"))
        {
            configuration.CaptureCurrentAsDefaults();
            statusMessage = "Saved current settings as defaults.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset to Saved Defaults"))
        {
            configuration.ApplySavedDefaults();
            statusMessage = "Restored settings from saved defaults.";
        }
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.45f, 0.14f, 0.14f, 1f));
        if (ImGui.Button("Reset to Factory Defaults"))
            ImGui.OpenPopup("##slidecaster-factory-reset");
        ImGui.PopStyleColor(3);

        if (ImGui.BeginPopupModal("##slidecaster-factory-reset", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped("This will reset every Slidecaster setting to the original factory values AND overwrite your \"Save Current as Defaults\" snapshot.");
            ImGui.TextWrapped("This cannot be undone. Continue?");
            ImGui.Spacing();
            if (ImGui.Button("Yes, reset everything", new Vector2(180f, 0f)))
            {
                configuration.ResetToFactoryDefaults();
                statusMessage = "All settings reset to factory defaults.";
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(80f, 0f)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (!string.IsNullOrEmpty(statusMessage))
            ImGui.TextDisabled(statusMessage);

        ImGui.Spacing();
        ImGui.TextDisabled($"Current safe trigger: {overlayWindow.GetConfiguredSafeWindowMs()}ms before cast end");
        ImGui.TextDisabled("Overlay appears with your cast bar and hides immediately when casting ends.");
    }
}
