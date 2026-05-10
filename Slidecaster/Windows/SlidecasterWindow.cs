using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace Slidecaster.Windows;

public sealed class SlidecasterWindow : Window
{
    private readonly Configuration configuration;
    private readonly CastbarOverlayWindow overlayWindow;

    public SlidecasterWindow(Configuration configuration, CastbarOverlayWindow overlayWindow)
        : base("Slidecaster Settings##window")
    {
        this.configuration = configuration;
        this.overlayWindow = overlayWindow;

        Size = new Vector2(460f, 250f);
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
        if (ImGui.SliderFloat("Overlay Opacity", ref opacity, 0.15f, 0.90f, "%.2f"))
        {
            configuration.OverlayOpacity = opacity;
            configuration.Save();
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

        ImGui.Spacing();
        ImGui.TextDisabled($"Current safe trigger: {overlayWindow.GetConfiguredSafeWindowMs()}ms before cast end");
        ImGui.TextDisabled("Overlay appears with your cast bar and hides immediately when casting ends.");
    }
}
