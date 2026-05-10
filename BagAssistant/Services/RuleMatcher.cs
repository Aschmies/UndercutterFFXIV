using System;
using System.Linq;

namespace BagAssistant.Services;

public static class RuleMatcher
{
    public static bool Matches(SortRule rule, InventoryItemInfo item)
    {
        if (!rule.Enabled) return false;

        // Item ID whitelist takes precedence (if set, only those items match).
        if (rule.ItemIdWhitelist.Count > 0 && !rule.ItemIdWhitelist.Contains(item.ItemId))
            return false;

        if (rule.ItemIdBlacklist.Contains(item.ItemId))
            return false;

        if (rule.ItemUICategories.Count > 0 && !rule.ItemUICategories.Contains(item.UICategoryRowId))
            return false;

        if (rule.Rarities.Count > 0 && !rule.Rarities.Contains(item.Rarity))
            return false;

        if (rule.MinEquipLevel > 0 || rule.MaxEquipLevel > 0)
        {
            var max = rule.MaxEquipLevel > 0 ? rule.MaxEquipLevel : int.MaxValue;
            if (item.EquipLevel < (uint)rule.MinEquipLevel || item.EquipLevel > (uint)max)
                return false;
        }

        if (rule.MinItemLevel > 0 || rule.MaxItemLevel > 0)
        {
            var max = rule.MaxItemLevel > 0 ? rule.MaxItemLevel : int.MaxValue;
            if (item.ItemLevel < (uint)rule.MinItemLevel || item.ItemLevel > (uint)max)
                return false;
        }

        if (rule.ClassJobs.Count > 0 && !item.ClassJobs.Any(j => rule.ClassJobs.Contains(j)))
            return false;

        if (rule.HQ == HQMatch.HQOnly && !item.IsHQ) return false;
        if (rule.HQ == HQMatch.NQOnly && item.IsHQ) return false;

        if (!MatchBool(rule.Untradeable, item.IsUntradeable)) return false;
        if (!MatchBool(rule.Collectable, item.IsCollectable)) return false;
        if (!MatchBool(rule.Stackable, item.IsStackable)) return false;
        if (!MatchBool(rule.Equippable, item.IsEquippable)) return false;

        if (!string.IsNullOrEmpty(rule.NameContains)
            && item.Name.IndexOf(rule.NameContains, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (rule.MinVendorPrice > 0 || rule.MaxVendorPrice > 0)
        {
            var max = rule.MaxVendorPrice > 0 ? rule.MaxVendorPrice : uint.MaxValue;
            if (item.VendorPrice < rule.MinVendorPrice || item.VendorPrice > max) return false;
        }

        return true;
    }

    private static bool MatchBool(BoolMatch m, bool val) => m switch
    {
        BoolMatch.Yes => val,
        BoolMatch.No  => !val,
        _             => true,
    };
}
