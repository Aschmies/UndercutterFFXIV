using FFXIVClientStructs.FFXIV.Client.Game;

namespace BagAssistant.Services;

/// <summary>
/// One pending move/swap. <see cref="DestSlot"/> = -1 means "first free slot in DestBag";
/// any non-negative value forces a specific slot (which will swap with whatever is currently there).
/// </summary>
public sealed record SortMove(
    InventoryType SrcBag,
    int SrcSlot,
    InventoryType DestBag,
    int DestSlot,
    string Label);
