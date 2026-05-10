using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BagAssistant.Services;

public record InventoryItemInfo(
    uint ItemId,
    string Name,
    InventoryType Container,
    int Slot,
    bool IsHQ,
    bool IsCollectable,
    bool IsUntradeable,
    bool IsStackable,
    bool IsEquippable,
    byte Rarity,
    uint EquipLevel,
    uint ItemLevel,
    uint VendorPrice,
    uint UICategoryRowId,
    string[] ClassJobs,
    int StackCount);

public sealed unsafe class InventoryService(IDataManager dataManager, IPluginLog log)
{
    public static readonly InventoryType[] PlayerBags =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    public List<InventoryItemInfo> ScanBags(IReadOnlyList<bool> bagFlags)
    {
        var results = new List<InventoryItemInfo>();
        var sheet = dataManager.GetExcelSheet<Item>();
        if (sheet == null) return results;

        var mgr = InventoryManager.Instance();
        if (mgr == null) return results;

        for (var b = 0; b < PlayerBags.Length; b++)
        {
            if (b < bagFlags.Count && !bagFlags[b]) continue;
            var invType = PlayerBags[b];
            var container = mgr->GetInventoryContainer(invType);
            if (container == null) continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var inv = container->GetInventorySlot(slot);
                if (inv == null || inv->ItemId == 0) continue;

                var data = sheet.GetRow(inv->ItemId);
                if (data.RowId == 0) continue;

                var isHQ = (inv->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
                var isCollectable = (inv->Flags & InventoryItem.ItemFlags.Collectable) != 0;

                results.Add(new InventoryItemInfo(
                    inv->ItemId,
                    data.Name.ToString(),
                    invType,
                    slot,
                    isHQ,
                    isCollectable,
                    data.IsUntradable,
                    data.StackSize > 1,
                    data.EquipSlotCategory.RowId != 0,
                    data.Rarity,
                    data.LevelEquip,
                    data.LevelItem.RowId,
                    data.PriceLow,
                    data.ItemUICategory.RowId,
                    GetClassJobs(data),
                    inv->Quantity
                ));
            }
        }

        return results;
    }

    public bool TryFindFreeSlot(InventoryType bag, out int slot)
    {
        slot = -1;
        var mgr = InventoryManager.Instance();
        if (mgr == null) return false;
        var c = mgr->GetInventoryContainer(bag);
        if (c == null) return false;
        for (var i = 0; i < c->Size; i++)
        {
            var s = c->GetInventorySlot(i);
            if (s != null && s->ItemId == 0)
            {
                slot = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>Move (or swap) an item into the destination bag's first free slot, or into a specific occupied slot to swap.</summary>
    public (bool Success, string Message) MoveOrSwap(InventoryItemInfo item, InventoryType destBag, int? destSlotOverride = null)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return (false, "InventoryManager unavailable.");

        var destSlot = destSlotOverride ?? -1;
        if (destSlot < 0)
        {
            // Prefer free slot
            if (TryFindFreeSlot(destBag, out var free))
                destSlot = free;
            else
                destSlot = 0; // forces a swap with the first slot
        }

        // No-op if already in place
        if (item.Container == destBag && item.Slot == destSlot)
            return (true, "Already at destination.");

        var result = mgr->MoveItemSlot(item.Container, (ushort)item.Slot, destBag, (ushort)destSlot, true);
        if (result == 0)
        {
            log.Debug($"[BagAssistant] Moved '{item.Name}' {item.Container}[{item.Slot}] -> {destBag}[{destSlot}]");
            return (true, $"Moved '{item.Name}'.");
        }

        return (false, $"Move failed (code {result}) for '{item.Name}'.");
    }

    private static string[] GetClassJobs(Item item)
    {
        var cat = item.ClassJobCategory.Value;
        var jobs = new List<string>();
        if (cat.GLA) jobs.Add("GLA");
        if (cat.PGL) jobs.Add("PGL");
        if (cat.MRD) jobs.Add("MRD");
        if (cat.LNC) jobs.Add("LNC");
        if (cat.ARC) jobs.Add("ARC");
        if (cat.CNJ) jobs.Add("CNJ");
        if (cat.THM) jobs.Add("THM");
        if (cat.ACN) jobs.Add("ACN");
        if (cat.ROG) jobs.Add("ROG");
        if (cat.PLD) jobs.Add("PLD");
        if (cat.MNK) jobs.Add("MNK");
        if (cat.WAR) jobs.Add("WAR");
        if (cat.DRG) jobs.Add("DRG");
        if (cat.BRD) jobs.Add("BRD");
        if (cat.WHM) jobs.Add("WHM");
        if (cat.BLM) jobs.Add("BLM");
        if (cat.SMN) jobs.Add("SMN");
        if (cat.SCH) jobs.Add("SCH");
        if (cat.NIN) jobs.Add("NIN");
        if (cat.MCH) jobs.Add("MCH");
        if (cat.DRK) jobs.Add("DRK");
        if (cat.AST) jobs.Add("AST");
        if (cat.SAM) jobs.Add("SAM");
        if (cat.RDM) jobs.Add("RDM");
        if (cat.BLU) jobs.Add("BLU");
        if (cat.GNB) jobs.Add("GNB");
        if (cat.DNC) jobs.Add("DNC");
        if (cat.RPR) jobs.Add("RPR");
        if (cat.SGE) jobs.Add("SGE");
        if (cat.VPR) jobs.Add("VPR");
        if (cat.PCT) jobs.Add("PCT");
        if (cat.CRP) jobs.Add("CRP");
        if (cat.BSM) jobs.Add("BSM");
        if (cat.ARM) jobs.Add("ARM");
        if (cat.GSM) jobs.Add("GSM");
        if (cat.LTW) jobs.Add("LTW");
        if (cat.WVR) jobs.Add("WVR");
        if (cat.ALC) jobs.Add("ALC");
        if (cat.CUL) jobs.Add("CUL");
        if (cat.MIN) jobs.Add("MIN");
        if (cat.BTN) jobs.Add("BTN");
        if (cat.FSH) jobs.Add("FSH");
        return jobs.ToArray();
    }
}
