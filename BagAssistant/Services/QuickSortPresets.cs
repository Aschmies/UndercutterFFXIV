using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BagAssistant.Services;

/// <summary>One destination assignment in a quick-sort plan.</summary>
public sealed class QuickMove
{
    public required InventoryItemInfo Item { get; init; }
    public required InventoryType DestBag { get; init; }
}

/// <summary>A one-click sort preset: a filter + destination bag with a friendly name and description.</summary>
public sealed class QuickPreset
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Func<InventoryItemInfo, bool> Match { get; init; }
    public required int DestBagIndex { get; init; }
}

public static class QuickSortPresets
{
    // ItemUICategory row IDs (Lumina). Common ones:
    private const uint UICategoryMeal = 46;
    private const uint UICategoryMedicine = 44;
    private const uint UICategoryCrystal = 59;
    private const uint UICategoryMateria = 58;

    // Reagents / Ingredients / Cooking ingredients live in many categories — easier to detect via flags below.

    public static readonly QuickPreset GroupGearToBag1 = new()
    {
        Name = "Gear → Bag 1",
        Description = "Move every equippable item (weapons, armor, accessories) into Bag 1.",
        Match = i => i.IsEquippable,
        DestBagIndex = 0,
    };

    public static readonly QuickPreset GroupHQGearToBag1 = new()
    {
        Name = "HQ Gear → Bag 1",
        Description = "Move only HQ equippable gear into Bag 1.",
        Match = i => i.IsEquippable && i.IsHQ,
        DestBagIndex = 0,
    };

    public static readonly QuickPreset GroupConsumablesToBag2 = new()
    {
        Name = "Food + Medicine → Bag 2",
        Description = "Move meals and medicine (potions, tinctures) into Bag 2.",
        Match = i => i.UICategoryRowId == UICategoryMeal || i.UICategoryRowId == UICategoryMedicine,
        DestBagIndex = 1,
    };

    public static readonly QuickPreset GroupCrystalsToBag4 = new()
    {
        Name = "Crystals + Materia → Bag 4",
        Description = "Move shards / crystals / clusters and materia into Bag 4.",
        Match = i => i.UICategoryRowId == UICategoryCrystal || i.UICategoryRowId == UICategoryMateria,
        DestBagIndex = 3,
    };

    public static readonly QuickPreset GroupMaterialsToBag3 = new()
    {
        Name = "Crafting Materials → Bag 3",
        Description = "Move stackable, non-equippable, non-consumable, non-crystal items (the bulk of crafting mats) into Bag 3.",
        Match = i => i.IsStackable
                     && !i.IsEquippable
                     && i.UICategoryRowId != UICategoryMeal
                     && i.UICategoryRowId != UICategoryMedicine
                     && i.UICategoryRowId != UICategoryCrystal
                     && i.UICategoryRowId != UICategoryMateria,
        DestBagIndex = 2,
    };

    public static readonly QuickPreset GroupCollectablesToBag3 = new()
    {
        Name = "Collectables → Bag 3",
        Description = "Move all collectables (gathering / crafting turn-ins) into Bag 3.",
        Match = i => i.IsCollectable,
        DestBagIndex = 2,
    };

    /// <summary>The full preset list shown in the Quick Sort tab.</summary>
    public static readonly QuickPreset[] All =
    [
        GroupGearToBag1,
        GroupHQGearToBag1,
        GroupConsumablesToBag2,
        GroupMaterialsToBag3,
        GroupCollectablesToBag3,
        GroupCrystalsToBag4,
    ];

    /// <summary>
    /// Builds a list of moves for the "Smart Sort Everything" preset:
    /// Gear → Bag1, Consumables → Bag2, Crystals/Materia → Bag4, everything stackable else → Bag3.
    /// First-match-wins ordering.
    /// </summary>
    public static List<QuickMove> BuildSmartSortPlan(IReadOnlyList<InventoryItemInfo> items, IReadOnlyList<bool> bagFlags)
    {
        var presetsInOrder = new[]
        {
            GroupHQGearToBag1,    // HQ gear takes precedence over plain gear (still goes to Bag 1)
            GroupGearToBag1,
            GroupConsumablesToBag2,
            GroupCrystalsToBag4,
            GroupMaterialsToBag3,
        };
        var result = new List<QuickMove>();
        foreach (var item in items)
        {
            foreach (var p in presetsInOrder)
            {
                if (!p.Match(item)) continue;
                var idx = Math.Clamp(p.DestBagIndex, 0, 3);
                if (idx >= bagFlags.Count || !bagFlags[idx]) break;
                var destBag = InventoryService.PlayerBags[idx];
                if (item.Container == destBag) break; // already there
                result.Add(new QuickMove { Item = item, DestBag = destBag });
                break;
            }
        }
        return result;
    }

    public static List<QuickMove> BuildPresetPlan(QuickPreset preset, IReadOnlyList<InventoryItemInfo> items, IReadOnlyList<bool> bagFlags)
    {
        var idx = Math.Clamp(preset.DestBagIndex, 0, 3);
        if (idx >= bagFlags.Count || !bagFlags[idx]) return new List<QuickMove>();
        var destBag = InventoryService.PlayerBags[idx];
        return items
            .Where(i => preset.Match(i) && i.Container != destBag)
            .Select(i => new QuickMove { Item = i, DestBag = destBag })
            .ToList();
    }
}
