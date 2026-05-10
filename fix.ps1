$content = Get-Content BagAssistant\Services\QuickSortPresets.cs -Raw
$content = $content -replace 'public static bool IsJunk\(InventoryItemInfo i, int maxVendorPrice, bool excludeCrafting\)[\s\S]*?return true;[\s\S]*?\}', "public static bool IsJunk(InventoryItemInfo i, Configuration config)
    {
        if (i.Rarity != 1) return false;
        if (config.ExcludeGearFromJunk && i.IsEquippable) return false;
        if (i.UICategoryRowId == 59 || i.UICategoryRowId == 58) return false;
        if (config.ExcludeConsumablesFromJunk && (i.UICategoryRowId == 44 || i.UICategoryRowId == 46)) return false;
        if (config.ExcludeCraftingFromJunk && i.IsStackable) return false;
        if (i.VendorPrice > config.MaxJunkVendorPrice) return false;
        return true;
    }"
Set-Content BagAssistant\Services\QuickSortPresets.cs -Value $content
