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

    // Volume of the safe-move sound cue (0.0 silent .. 1.0 full).
    public float SafeCueVolume { get; set; } = 0.6f;

    // Overlay styling
    public float OverlayOpacity { get; set; } = 0.45f;
    public float OverlayHeightScale { get; set; } = 1.0f;
    public float SafeBarHeightScale { get; set; } = 1.0f;
    public bool ShowCastBarBorder { get; set; } = true;

    // Overlay safe-zone fill color (linear 0..1).
    public float OverlayColorR { get; set; } = 0.10f;
    public float OverlayColorG { get; set; } = 0.90f;
    public float OverlayColorB { get; set; } = 0.30f;

    // Trim only the right edge of the overlay so it can align exactly with cast bar visuals.
    public float OverlayEndTrimPx { get; set; } = 0f;

    // Visual-only adjustment for where the moving progress marker starts.
    public bool EnableProgressMarkerStartOffset { get; set; } = false;
    public float ProgressMarkerStartOffsetPx { get; set; } = 0f;

    // User-defined defaults snapshot.
    public int DefaultBaseSafeWindowMs { get; set; } = 500;
    public int DefaultLatencyCompensationMs { get; set; } = 0;
    public bool DefaultShowSafeText { get; set; } = true;
    public bool DefaultPlaySafeMoveSound { get; set; } = false;
    public float DefaultSafeCueVolume { get; set; } = 0.6f;
    public float DefaultOverlayOpacity { get; set; } = 0.45f;
    public float DefaultOverlayHeightScale { get; set; } = 1.0f;
    public float DefaultSafeBarHeightScale { get; set; } = 1.0f;
    public bool DefaultShowCastBarBorder { get; set; } = true;
    public float DefaultOverlayColorR { get; set; } = 0.10f;
    public float DefaultOverlayColorG { get; set; } = 0.90f;
    public float DefaultOverlayColorB { get; set; } = 0.30f;
    public float DefaultOverlayEndTrimPx { get; set; } = 0f;
    public bool DefaultEnableProgressMarkerStartOffset { get; set; } = false;
    public float DefaultProgressMarkerStartOffsetPx { get; set; } = 0f;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface plugin) => pluginInterface = plugin;

    public void CaptureCurrentAsDefaults()
    {
        DefaultBaseSafeWindowMs = BaseSafeWindowMs;
        DefaultLatencyCompensationMs = LatencyCompensationMs;
        DefaultShowSafeText = ShowSafeText;
        DefaultPlaySafeMoveSound = PlaySafeMoveSound;
        DefaultSafeCueVolume = SafeCueVolume;
        DefaultOverlayOpacity = OverlayOpacity;
        DefaultOverlayHeightScale = OverlayHeightScale;
        DefaultSafeBarHeightScale = SafeBarHeightScale;
        DefaultShowCastBarBorder = ShowCastBarBorder;
        DefaultOverlayColorR = OverlayColorR;
        DefaultOverlayColorG = OverlayColorG;
        DefaultOverlayColorB = OverlayColorB;
        DefaultOverlayEndTrimPx = OverlayEndTrimPx;
        DefaultEnableProgressMarkerStartOffset = EnableProgressMarkerStartOffset;
        DefaultProgressMarkerStartOffsetPx = ProgressMarkerStartOffsetPx;
        Save();
    }

    public void ApplySavedDefaults()
    {
        BaseSafeWindowMs = DefaultBaseSafeWindowMs;
        LatencyCompensationMs = DefaultLatencyCompensationMs;
        ShowSafeText = DefaultShowSafeText;
        PlaySafeMoveSound = DefaultPlaySafeMoveSound;
        SafeCueVolume = DefaultSafeCueVolume;
        OverlayOpacity = DefaultOverlayOpacity;
        OverlayHeightScale = DefaultOverlayHeightScale;
        SafeBarHeightScale = DefaultSafeBarHeightScale;
        ShowCastBarBorder = DefaultShowCastBarBorder;
        OverlayColorR = DefaultOverlayColorR;
        OverlayColorG = DefaultOverlayColorG;
        OverlayColorB = DefaultOverlayColorB;
        OverlayEndTrimPx = DefaultOverlayEndTrimPx;
        EnableProgressMarkerStartOffset = DefaultEnableProgressMarkerStartOffset;
        ProgressMarkerStartOffsetPx = DefaultProgressMarkerStartOffsetPx;
        Save();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
