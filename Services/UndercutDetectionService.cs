using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using UndercutterFFXIV.Models;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Monitors listed items for undercuts and sends chat notifications
    /// </summary>
    public class UndercutDetectionService
    {
        private readonly UniversalisMarketClient universalis;
        private readonly IChatGui chatGui;
        private readonly Configuration configuration;
        private readonly RetainerPriceService retainerPriceService;
        private readonly object lockObj = new();
        private Dictionary<uint, UndercutHistory> undercutHistory = new();

        private class UndercutHistory
        {
            public uint ItemId { get; set; }
            public string ItemName { get; set; } = "";
            public uint LastNotifiedPrice { get; set; }
            public DateTime LastNotificationTime { get; set; }
        }

        public UndercutDetectionService(
            UniversalisMarketClient universalis,
            IChatGui chatGui,
            Configuration configuration,
            RetainerPriceService retainerPriceService)
        {
            this.universalis = universalis;
            this.chatGui = chatGui;
            this.configuration = configuration;
            this.retainerPriceService = retainerPriceService;
        }

        /// <summary>
        /// Check current listed items for undercuts
        /// </summary>
        public async Task CheckForUndercutsAsync(string worldName, CancellationToken cancellationToken = default)
        {
            if (!configuration.AlertOnUndercut)
                return;

            try
            {
                var listings = retainerPriceService.GetCurrentSellingListings();
                if (listings.Count == 0)
                    return;

                foreach (var listing in listings)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await CheckItemUndercutAsync(worldName, listing, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Undercut detection failed: {ex.Message}");
            }
        }

        private async Task CheckItemUndercutAsync(string worldName, RetainerSaleListing listing, CancellationToken cancellationToken)
        {
            try
            {
                var marketData = await universalis.GetMarketSnapshotAsync(worldName, listing.ItemId, cancellationToken);
                if (marketData == null || marketData.LowestPrice == 0)
                    return;

                lock (lockObj)
                {
                    bool wasUndercut = false;
                    uint priceDiff = 0;

                    if (marketData.LowestPrice < listing.CurrentPrice)
                    {
                        priceDiff = listing.CurrentPrice - marketData.LowestPrice;
                        wasUndercut = true;
                    }

                    // Check if we should notify (debounce repeated notifications)
                    var shouldNotify = ShouldNotifyAboutUndercut(listing.ItemId, listing.Name, marketData.LowestPrice);

                    if (wasUndercut && shouldNotify)
                    {
                        var message = $"⚠️ UNDERCUT: {listing.Name} - Your: {listing.CurrentPrice} → Market: {marketData.LowestPrice} (-{priceDiff} gil)";
                        SendChatMessage(message);
                        RecordNotification(listing.ItemId, listing.Name, marketData.LowestPrice);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to check undercut for {listing.Name}: {ex.Message}");
            }
        }

        private bool ShouldNotifyAboutUndercut(uint itemId, string itemName, uint lowestPrice)
        {
            if (!undercutHistory.ContainsKey(itemId))
                return true;

            var history = undercutHistory[itemId];

            // Don't notify if we just notified about this item in the last minute
            var timeSinceLastNotification = DateTime.UtcNow - history.LastNotificationTime;
            if (timeSinceLastNotification.TotalSeconds < 60)
                return false;

            // Notify if price changed significantly (more than 5 gil difference)
            if (Math.Abs((int)lowestPrice - (int)history.LastNotifiedPrice) >= 5)
                return true;

            return false;
        }

        private void RecordNotification(uint itemId, string itemName, uint price)
        {
            undercutHistory[itemId] = new UndercutHistory
            {
                ItemId = itemId,
                ItemName = itemName,
                LastNotifiedPrice = price,
                LastNotificationTime = DateTime.UtcNow
            };
        }

        private void SendChatMessage(string message)
        {
            try
            {
                // Use the Echo chat type to send a system message
                chatGui.Print(message);
                LoggingService.LogInfo($"Chat notification: {message}");
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to send chat message: {ex.Message}");
            }
        }
    }
}
