using Dalamud.Configuration;
using Dalamud.Plugin;

namespace CombatStatistics.Config;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool MergePetsWithOwner { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public bool ShowMainWindowOnStart { get; set; } = true;
    public bool ShowDebugInfo { get; set; } = false;
    public int EncounterTimeoutSeconds { get; set; } = 10;
    public int OverlayRowCount { get; set; } = 6;
    public float OverlayOpacity { get; set; } = 0.78f;
    public float OverlayFontSize { get; set; } = 14f;
    public bool OverlayUsePerMinuteMetrics { get; set; } = false;

    public float OverlayWindowX { get; set; } = 80f;
    public float OverlayWindowY { get; set; } = 180f;
    public float MainWindowX { get; set; } = 240f;
    public float MainWindowY { get; set; } = 120f;

    public float OverlayBackgroundR { get; set; } = 0.08f;
    public float OverlayBackgroundG { get; set; } = 0.09f;
    public float OverlayBackgroundB { get; set; } = 0.12f;
    public float OverlayAccentR { get; set; } = 0.30f;
    public float OverlayAccentG { get; set; } = 0.82f;
    public float OverlayAccentB { get; set; } = 0.98f;

    public float MainWindowWidth { get; set; } = 860f;
    public float MainWindowHeight { get; set; } = 540f;

    public float DefaultOverlayOpacity { get; set; } = 0.78f;
    public float DefaultOverlayFontSize { get; set; } = 14f;
    public int DefaultOverlayRowCount { get; set; } = 6;
    public int DefaultEncounterTimeoutSeconds { get; set; } = 10;
    public bool DefaultMergePetsWithOwner { get; set; } = true;
    public bool DefaultShowOverlay { get; set; } = true;
    public bool DefaultShowMainWindowOnStart { get; set; } = true;
    public bool DefaultOverlayUsePerMinuteMetrics { get; set; } = false;
    public float DefaultOverlayWindowX { get; set; } = 80f;
    public float DefaultOverlayWindowY { get; set; } = 180f;
    public float DefaultMainWindowX { get; set; } = 240f;
    public float DefaultMainWindowY { get; set; } = 120f;
    public float DefaultMainWindowWidth { get; set; } = 860f;
    public float DefaultMainWindowHeight { get; set; } = 540f;
    public float DefaultOverlayBackgroundR { get; set; } = 0.08f;
    public float DefaultOverlayBackgroundG { get; set; } = 0.09f;
    public float DefaultOverlayBackgroundB { get; set; } = 0.12f;
    public float DefaultOverlayAccentR { get; set; } = 0.30f;
    public float DefaultOverlayAccentG { get; set; } = 0.82f;
    public float DefaultOverlayAccentB { get; set; } = 0.98f;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface plugin) => pluginInterface = plugin;

    public void CaptureCurrentAsDefaults()
    {
        DefaultMergePetsWithOwner = MergePetsWithOwner;
        DefaultShowOverlay = ShowOverlay;
        DefaultShowMainWindowOnStart = ShowMainWindowOnStart;
        DefaultEncounterTimeoutSeconds = EncounterTimeoutSeconds;
        DefaultOverlayRowCount = OverlayRowCount;
        DefaultOverlayOpacity = OverlayOpacity;
        DefaultOverlayFontSize = OverlayFontSize;
        DefaultOverlayUsePerMinuteMetrics = OverlayUsePerMinuteMetrics;
        DefaultOverlayWindowX = OverlayWindowX;
        DefaultOverlayWindowY = OverlayWindowY;
        DefaultMainWindowX = MainWindowX;
        DefaultMainWindowY = MainWindowY;
        DefaultMainWindowWidth = MainWindowWidth;
        DefaultMainWindowHeight = MainWindowHeight;
        DefaultOverlayBackgroundR = OverlayBackgroundR;
        DefaultOverlayBackgroundG = OverlayBackgroundG;
        DefaultOverlayBackgroundB = OverlayBackgroundB;
        DefaultOverlayAccentR = OverlayAccentR;
        DefaultOverlayAccentG = OverlayAccentG;
        DefaultOverlayAccentB = OverlayAccentB;
        Save();
    }

    public void ApplySavedDefaults()
    {
        MergePetsWithOwner = DefaultMergePetsWithOwner;
        ShowOverlay = DefaultShowOverlay;
        ShowMainWindowOnStart = DefaultShowMainWindowOnStart;
        EncounterTimeoutSeconds = DefaultEncounterTimeoutSeconds;
        OverlayRowCount = DefaultOverlayRowCount;
        OverlayOpacity = DefaultOverlayOpacity;
        OverlayFontSize = DefaultOverlayFontSize;
        OverlayUsePerMinuteMetrics = DefaultOverlayUsePerMinuteMetrics;
        OverlayWindowX = DefaultOverlayWindowX;
        OverlayWindowY = DefaultOverlayWindowY;
        MainWindowX = DefaultMainWindowX;
        MainWindowY = DefaultMainWindowY;
        MainWindowWidth = DefaultMainWindowWidth;
        MainWindowHeight = DefaultMainWindowHeight;
        OverlayBackgroundR = DefaultOverlayBackgroundR;
        OverlayBackgroundG = DefaultOverlayBackgroundG;
        OverlayBackgroundB = DefaultOverlayBackgroundB;
        OverlayAccentR = DefaultOverlayAccentR;
        OverlayAccentG = DefaultOverlayAccentG;
        OverlayAccentB = DefaultOverlayAccentB;
        Save();
    }

    public void ResetToFactoryDefaults()
    {
        MergePetsWithOwner = true;
        ShowOverlay = true;
        ShowMainWindowOnStart = true;
        EncounterTimeoutSeconds = 10;
        OverlayRowCount = 6;
        OverlayOpacity = 0.78f;
        OverlayFontSize = 14f;
        OverlayUsePerMinuteMetrics = false;
        OverlayWindowX = 80f;
        OverlayWindowY = 180f;
        MainWindowX = 240f;
        MainWindowY = 120f;
        MainWindowWidth = 860f;
        MainWindowHeight = 540f;
        OverlayBackgroundR = 0.08f;
        OverlayBackgroundG = 0.09f;
        OverlayBackgroundB = 0.12f;
        OverlayAccentR = 0.30f;
        OverlayAccentG = 0.82f;
        OverlayAccentB = 0.98f;

        CaptureCurrentAsDefaults();
        Save();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
