using Dalamud.Configuration;
using Dalamud.Plugin;

namespace MinimapPlugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Window
    public bool IsVisible { get; set; } = true;
    public float WindowX { get; set; } = 20f;
    public float WindowY { get; set; } = 20f;
    public float WindowSize { get; set; } = 220f;
    public float Opacity { get; set; } = 0.9f;

    // Zoom
    public float ZoomLevel { get; set; } = 1.0f;
    public float ZoomMin { get; set; } = 0.5f;
    public float ZoomMax { get; set; } = 4.0f;

    // Rotation
    public bool RotateWithPlayer { get; set; } = false;

    // Markers
    public bool ShowPartyMembers { get; set; } = true;
    public bool ShowOtherPlayers { get; set; } = false;
    public bool ShowQuestMarkers { get; set; } = true;
    public bool ShowFates { get; set; } = true;
    public bool ShowAetherytes { get; set; } = true;

    // Behaviour
    public bool HideInCutscenes { get; set; } = true;
    public bool HideInDuties { get; set; } = false;
    public bool ClickThrough { get; set; } = false;
    public bool LockPosition { get; set; } = false;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
