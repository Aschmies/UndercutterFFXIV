using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace UndercutterFFXIV.Services
{
    public sealed unsafe class RetainerPriceService
    {
        private readonly IGameGui gameGui;

        public RetainerPriceService(IGameGui gameGui)
        {
            this.gameGui = gameGui;
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