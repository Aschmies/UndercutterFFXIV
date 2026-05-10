using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Slidecaster;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // The classic slidecast window is about the last 0.5s; users can tune this for comfort.
    public int BaseSafeWindowMs { get; set; } = 500;

    // Optional manual latency compensation if you want a slightly earlier safe cue.
    public int LatencyCompensationMs { get; set; } = 0;

    public bool ShowSafeText { get; set; } = true;
    public bool PlaySafeMoveSound { get; set; } = false;

    // Overlay styling
    public float OverlayOpacity { get; set; } = 0.45f;
    public float OverlayHeightScale { get; set; } = 1.0f;
    public float SafeBarHeightScale { get; set; } = 1.0f;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface plugin) => pluginInterface = plugin;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
