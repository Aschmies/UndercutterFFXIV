using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
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

        public IReadOnlyList<RetainerSaleListing> GetCurrentSellingListings()
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return Array.Empty<RetainerSaleListing>();

            var container = inventoryManager->GetInventoryContainer(InventoryType.RetainerMarket);
            if (container == null || !container->IsLoaded || container->Size <= 0)
                return Array.Empty<RetainerSaleListing>();

            var itemSheet = dataManager.GetExcelSheet<Item>();
            if (itemSheet == null)
                return Array.Empty<RetainerSaleListing>();

            var results = new List<RetainerSaleListing>();

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

                results.Add(new RetainerSaleListing
                {
                    SlotIndex = index,
                    ItemId = itemId,
                    Name = name,
                    CurrentPrice = currentPrice
                });
            }

            return results
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SlotIndex)
                .ToList();
        }

        public bool IsRetainerSellWindowOpen()
        {
            var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
            return addon != null && addon->AskingPrice != null;
        }

        public bool TryAutoFillPrice(uint price, out string status)
        {
            var addon = gameGui.GetAddonByName<AddonRetainerSell>("RetainerSell");
            if (addon == null || addon->AskingPrice == null)
            {
                status = "Retainer price field not detected";
                return false;
            }

            addon->AskingPrice->InnerSetValue((int)price, true, true);
            status = "Auto-filled price into retainer window";
            return true;
        }
    }
}