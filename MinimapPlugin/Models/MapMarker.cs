namespace MinimapPlugin.Models;

public enum MarkerType
{
    PartyMember,
    OtherPlayer,
    QuestNpc,
    Fate,
    Aetheryte,
}

public sealed class MapMarker
{
    public float WorldX { get; set; }
    public float WorldZ { get; set; }
    public MarkerType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}
