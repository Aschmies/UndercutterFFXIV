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
    public bool ShowCastBarBorder { get; set; } = true;

    // Trim only the right edge of the overlay so it can align exactly with cast bar visuals.
    public float OverlayEndTrimPx { get; set; } = 0f;

    // User-defined defaults snapshot.
    public int DefaultBaseSafeWindowMs { get; set; } = 500;
    public int DefaultLatencyCompensationMs { get; set; } = 0;
    public bool DefaultShowSafeText { get; set; } = true;
    public bool DefaultPlaySafeMoveSound { get; set; } = false;
    public float DefaultOverlayOpacity { get; set; } = 0.45f;
    public float DefaultOverlayHeightScale { get; set; } = 1.0f;
    public float DefaultSafeBarHeightScale { get; set; } = 1.0f;
    public bool DefaultShowCastBarBorder { get; set; } = true;
    public float DefaultOverlayEndTrimPx { get; set; } = 0f;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface plugin) => pluginInterface = plugin;

    public void CaptureCurrentAsDefaults()
    {
        DefaultBaseSafeWindowMs = BaseSafeWindowMs;
        DefaultLatencyCompensationMs = LatencyCompensationMs;
        DefaultShowSafeText = ShowSafeText;
        DefaultPlaySafeMoveSound = PlaySafeMoveSound;
        DefaultOverlayOpacity = OverlayOpacity;
        DefaultOverlayHeightScale = OverlayHeightScale;
        DefaultSafeBarHeightScale = SafeBarHeightScale;
        DefaultShowCastBarBorder = ShowCastBarBorder;
        DefaultOverlayEndTrimPx = OverlayEndTrimPx;
        Save();
    }

    public void ApplySavedDefaults()
    {
        BaseSafeWindowMs = DefaultBaseSafeWindowMs;
        LatencyCompensationMs = DefaultLatencyCompensationMs;
        ShowSafeText = DefaultShowSafeText;
        PlaySafeMoveSound = DefaultPlaySafeMoveSound;
        OverlayOpacity = DefaultOverlayOpacity;
        OverlayHeightScale = DefaultOverlayHeightScale;
        SafeBarHeightScale = DefaultSafeBarHeightScale;
        ShowCastBarBorder = DefaultShowCastBarBorder;
        OverlayEndTrimPx = DefaultOverlayEndTrimPx;
        Save();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
