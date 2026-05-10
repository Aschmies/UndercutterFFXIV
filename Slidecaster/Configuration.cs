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

    public bool RoundRightSide { get; set; } = false;
    public bool DrawAsLine { get; set; } = false;
    public float LineThickness { get; set; } = 3.0f;
    public float LineHeightScale { get; set; } = 1.2f;

    // Visual-only adjustment for where the moving progress marker starts.
    public bool EnableProgressMarkerStartOffset { get; set; } = false;
    public float ProgressMarkerStartOffsetPx { get; set; } = 0f;

    // Vertical pixel offset applied to the overlay's Y center. Negative values raise the overlay
    // so it lines up with the visible cast progress bar rather than the addon's full bounding box.
    public float OverlayYOffsetPx { get; set; } = -8f;

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
    public bool DefaultRoundRightSide { get; set; } = false;
    public bool DefaultDrawAsLine { get; set; } = false;
    public float DefaultLineThickness { get; set; } = 3.0f;
    public float DefaultLineHeightScale { get; set; } = 1.2f;
    public bool DefaultEnableProgressMarkerStartOffset { get; set; } = false;
    public float DefaultProgressMarkerStartOffsetPx { get; set; } = 0f;
    public float DefaultOverlayYOffsetPx { get; set; } = -8f;

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
        DefaultRoundRightSide = RoundRightSide;
        DefaultDrawAsLine = DrawAsLine;
        DefaultLineThickness = LineThickness;
        DefaultLineHeightScale = LineHeightScale;
        DefaultEnableProgressMarkerStartOffset = EnableProgressMarkerStartOffset;
        DefaultProgressMarkerStartOffsetPx = ProgressMarkerStartOffsetPx;
        DefaultOverlayYOffsetPx = OverlayYOffsetPx;
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
        RoundRightSide = DefaultRoundRightSide;
        DrawAsLine = DefaultDrawAsLine;
        LineThickness = DefaultLineThickness;
        LineHeightScale = DefaultLineHeightScale;
        EnableProgressMarkerStartOffset = DefaultEnableProgressMarkerStartOffset;
        ProgressMarkerStartOffsetPx = DefaultProgressMarkerStartOffsetPx;
        OverlayYOffsetPx = DefaultOverlayYOffsetPx;
        Save();
    }

    /// <summary>
    /// Hard-resets every setting (current and the user-saved defaults snapshot) back to the
    /// values shipped with the plugin. This overrides any "Save Current as Defaults" the user
    /// has previously stored.
    /// </summary>
    public void ResetToFactoryDefaults()
    {
        // Live settings
        BaseSafeWindowMs = 500;
        LatencyCompensationMs = 0;
        ShowSafeText = true;
        PlaySafeMoveSound = false;
        SafeCueVolume = 0.6f;
        OverlayOpacity = 0.45f;
        OverlayHeightScale = 1.0f;
        SafeBarHeightScale = 1.0f;
        ShowCastBarBorder = true;
        OverlayColorR = 0.10f;
        OverlayColorG = 0.90f;
        OverlayColorB = 0.30f;
        OverlayEndTrimPx = 0f;
        RoundRightSide = false;
        DrawAsLine = false;
        LineThickness = 3.0f;
        LineHeightScale = 1.2f;
        EnableProgressMarkerStartOffset = false;
        ProgressMarkerStartOffsetPx = 0f;
        OverlayYOffsetPx = -8f;

        // Overwrite the user-saved defaults snapshot with the same factory values.
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
        DefaultRoundRightSide = RoundRightSide;
        DefaultDrawAsLine = DrawAsLine;
        DefaultLineThickness = LineThickness;
        DefaultLineHeightScale = LineHeightScale;
        DefaultEnableProgressMarkerStartOffset = EnableProgressMarkerStartOffset;
        DefaultProgressMarkerStartOffsetPx = ProgressMarkerStartOffsetPx;
        DefaultOverlayYOffsetPx = OverlayYOffsetPx;
        Save();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
