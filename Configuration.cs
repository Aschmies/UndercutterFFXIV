using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace UndercutterFFXIV
{
    [Serializable]
    public sealed class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        // Core scanner configuration
        public string WorldName { get; set; } = "Aether";
        public string DataCenterName { get; set; } = "Aether";
        public double MarketTaxRatePercent { get; set; } = 5.0;
        public int ScannerLookbackDays { get; set; } = 7;
        public double MinSaleVelocityPerDay { get; set; } = 2.0;
        public double GearMinVelocityPerDay { get; set; } = 1.0;
        public int MinUnitsSold24h { get; set; } = 0;
        public int MinNetProfitGil { get; set; } = 100;
        public double MinNetProfitPercent { get; set; } = 5.0;
        public int CheapItemPriceThresholdGil { get; set; } = 250;
        public int CheapItemMinProfitableQuantity { get; set; } = 3;

        // Manual helper configuration
        public uint UndercutAmount { get; set; } = 1;
        public bool EnableRetainerAutoFill { get; set; } = false;
        public bool AutoTrackCurrentlySellingItems { get; set; } = true;

        // Auto buy-history capture
        public bool EnableAutoBuyHistoryCapture { get; set; } = true;
        public bool AutoBuyHistoryAutoConfirm { get; set; } = false;

        // Undercut alert quality controls
        public int UndercutAlertCooldownSeconds { get; set; } = 300;
        public int UndercutAlertMinDeltaGil { get; set; } = 5;
        public double UndercutAlertRepeatDeltaPercent { get; set; } = 1.0;

        // World-travel planner
        public int WorldTravelOverheadGil { get; set; } = 250;
        public int WorldTravelMinNetGil { get; set; } = 1000;

        // Profiles and guardrails
        public string ActiveScanProfile { get; set; } = "Balanced";
        public bool EnableStrictRepriceGuardrails { get; set; } = true;
        public int MinRepriceMarginGil { get; set; } = 100;
        public double MinRepriceMarginPercent { get; set; } = 5.0;
        public bool EnableDegradedModeActionBlock { get; set; } = true;

        // Position sizing / capital caps
        public int MaxCapitalPerItemGil { get; set; } = 250000;
        public int MaxCapitalPerDayGil { get; set; } = 1500000;

        // Legacy fields kept for backward compatibility with existing windows/services
        public bool PluginEnabled { get; set; } = true;
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
        public bool UseUniversalisAPI { get; set; } = true;
        public int UniversalisRefreshMinutes { get; set; } = 5;
        public bool EnableSellSuggestions { get; set; } = true;
        public uint DefaultMinProfitPercent { get; set; } = 10;
        public uint DefaultMinProfitGil { get; set; } = 100;

        // Background polling (Universalis only)
        public bool EnableBackgroundPolling { get; set; } = false;
        public int PollingBaseSeconds { get; set; } = 300;
        public int PollingJitterSeconds { get; set; } = 15;

        // Optional session guard for background scans
        public bool EnableSessionLimit { get; set; } = true;
        public int SessionLimitHours { get; set; } = 4;

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            pluginInterface?.SavePluginConfig(this);
        }
    }
}
