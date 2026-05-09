using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    public sealed unsafe class RetainerPriceService
    {
        private readonly IGameGui gameGui;
        private readonly IDataManager dataManager;

        public RetainerPriceService(IGameGui gameGui, IDataManager dataManager)
        {
            this.gameGui = gameGui;
            this.dataManager = dataManager;
        }

        public IReadOnlyList<ItemLookup> GetCurrentSellingItems()
        {
            return GetCurrentSellingListings()
                .GroupBy(entry => entry.ItemId)
                .Select(group => new ItemLookup
                {
                    ItemId = group.Key,
                    Name = group.First().Name
                })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public int GetOwnedItemQuantity(uint itemId)
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null || itemId == 0)
                return 0;

            // Includes normal inventory + armoury in live character state.
            var owned = Math.Max(0, inventoryManager->GetInventoryItemCount(itemId, false, true, true, 0));

            // Add currently listed retainer-market quantity for stronger "already own this" prioritization.
            var listed = Math.Max(0, inventoryManager->GetItemCountInContainer(itemId, InventoryType.RetainerMarket, false, 0));
            return owned + listed;
        }

        public IReadOnlyList<RetainerSaleListing> GetCurrentSellingListings()
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return Array.Empty<RetainerSaleListing>();

            var container = inventoryManager->GetInventoryContainer(InventoryType.RetainerMarket);
            var itemSheet = dataManager.GetExcelSheet<Item>();
            if (itemSheet == null)
                return Array.Empty<RetainerSaleListing>();

            if (container != null && container->Size > 0)
            {
                var loadedResults = new List<RetainerSaleListing>();

                for (var index = 0; index < container->Size; index++)
                {
                    var slot = inventoryManager->GetInventorySlot(InventoryType.RetainerMarket, index);
                    if (slot == null || slot->IsEmpty())
                        continue;

                    var itemId = slot->GetBaseItemId();
                    if (itemId == 0)
                        continue;

                    var row = itemSheet.GetRow(itemId);
                    var name = row.Name.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var rawPrice = inventoryManager->GetRetainerMarketPrice((short)index);
                    var currentPrice = rawPrice > uint.MaxValue ? uint.MaxValue : (uint)rawPrice;

                    loadedResults.Add(new RetainerSaleListing
                    {
                        SlotIndex = index,
                        ItemId = itemId,
                        Name = name,
                        CurrentPrice = currentPrice
                    });
                }

                return loadedResults
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SlotIndex)
                    .ToList();
            }

            var cachedResults = new List<RetainerSaleListing>();

            foreach (var row in itemSheet)
            {
                if (row.RowId == 0)
                    continue;

                var count = inventoryManager->GetItemCountInContainer(row.RowId, InventoryType.RetainerMarket, false, 0);
                if (count <= 0)
                    continue;

                var name = row.Name.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                cachedResults.Add(new RetainerSaleListing
                {
                    SlotIndex = cachedResults.Count,
                    ItemId = row.RowId,
                    Name = name,
                    CurrentPrice = 0
                });
            }

            return cachedResults
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ItemId)
                .ToList();
        }

        public bool IsRetainerSellWindowOpen()
        {
            var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
            return addon != null && addon->AskingPrice != null;
        }

        public bool TryAutoFillPrice(uint price, out string status)
            => TryAutoSelectAdjustPriceAndFill(price, out status);

        public bool TryAutoSelectAdjustPriceAndFill(uint price, out string status)
        {
            var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
            if (addon == null)
            {
                status = "Retainer sell window not detected";
                return false;
            }

            if (addon->ComparePrices != null)
                addon->ComparePrices->ReceiveEvent(AtkEventType.ButtonClick, 0, null, null);

            if (addon->AskingPrice == null)
            {
                status = "Retainer price field not detected";
                return false;
            }

            addon->AskingPrice->InnerSetValue((int)price, true, true);
            status = "Selected adjust price and auto-filled retainer window";
            return true;
        }
    }
}