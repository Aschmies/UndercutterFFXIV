using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;
using System.Linq;

namespace BagAssistant.Services;

public sealed class SortPlanEntry
{
    public required InventoryItemInfo Item { get; init; }
    public required SortRule Rule { get; init; }
    public required InventoryType DestBag { get; init; }
}

/// <summary>
/// Builds and executes sort plans. Strategy: for every item in the included bags, find the first
/// matching rule whose target is a specific bag; if the item is not already in that bag, queue a
/// move into the lowest free slot in that bag (or a swap if the bag is full).
/// </summary>
public sealed class SortRunner(InventoryService inventoryService)
{
    public List<SortPlanEntry> BuildPlan(Configuration config)
    {
        var bagFlags = new[] { config.IncludeBag1, config.IncludeBag2, config.IncludeBag3, config.IncludeBag4 };
        var items = inventoryService.ScanBags(bagFlags);
        var plan = new List<SortPlanEntry>();

        foreach (var item in items)
        {
            SortRule? matched = null;
            foreach (var rule in config.Rules)
            {
                if (RuleMatcher.Matches(rule, item))
                {
                    matched = rule;
                    break;
                }
            }
            if (matched == null) continue;
            if (matched.Target != SortTarget.SpecificBag) continue;

            var destIdx = System.Math.Clamp(matched.TargetBagIndex, 0, 3);
            if (!bagFlags[destIdx]) continue; // skip rules that target excluded bags
            var destBag = InventoryService.PlayerBags[destIdx];
            if (item.Container == destBag) continue; // already in place

            plan.Add(new SortPlanEntry { Item = item, Rule = matched, DestBag = destBag });
        }

        return plan;
    }
}
