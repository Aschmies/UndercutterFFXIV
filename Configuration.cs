using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace UndercutterFFXIV
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public uint UndercutAmount { get; set; } = 1;
        public int CheckIntervalSeconds { get; set; } = 30;
        public double TaxPercentage { get; set; } = 5.0;
        public bool AlertOnUndercut { get; set; } = true;
        public bool EnableChatAlerts { get; set; } = true;
        public bool EnableToastNotifications { get; set; } = true;
        public bool EnableSoundAlerts { get; set; } = false;
        public int MaxAlertHistoryDays { get; set; } = 7;
        public int MaxPriceHistoryDays { get; set; } = 90;
        public bool AutoSyncRetainers { get; set; } = false;
        public int AutoSyncIntervalMinutes { get; set; } = 60;
        public bool ShowPriceHistory { get; set; } = true;
        public bool ShowProfitMargins { get; set; } = true;
        public int HistoryDisplayDays { get; set; } = 30;
        // Universalis API settings
        public bool UseUniversalisAPI { get; set; } = true;
        public int UniversalisRefreshMinutes { get; set; } = 5;
        public string WorldName { get; set; } = "Aether";
        
        // Flip/Sell suggestions
        public bool EnableSellSuggestions { get; set; } = true;
        public uint DefaultMinProfitPercent { get; set; } = 10;
        public uint DefaultMinProfitGil { get; set; } = 100;

        public bool PluginEnabled { get; set; } = true;

        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            PluginInterface = pluginInterface;
        }

        public void Save()
        {
            PluginInterface?.SavePluginConfig(this);
        }
    }
}
