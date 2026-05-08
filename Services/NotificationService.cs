using System;
using System.Collections.Generic;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Notification service for multi-channel alerts
    /// </summary>
    public class NotificationService
    {
        public event Action<string>? OnNotificationAdded;
        private List<string> recentNotifications = new();

        public void SendChatNotification(string message)
        {
            LoggingService.LogInfo($"Chat: {message}");
            OnNotificationAdded?.Invoke(message);
            recentNotifications.Add(message);
            if (recentNotifications.Count > 100)
                recentNotifications.RemoveAt(0);
        }

        public void SendToastNotification(string title, string message)
        {
            var fullMessage = $"{title}: {message}";
            LoggingService.LogInfo($"Toast: {fullMessage}");
            OnNotificationAdded?.Invoke(fullMessage);
            recentNotifications.Add(fullMessage);
        }

        public void PlaySoundAlert()
        {
            LoggingService.LogInfo("Sound alert triggered");
        }

        public List<string> GetRecentNotifications() => new List<string>(recentNotifications);
        public void ClearNotifications() => recentNotifications.Clear();
    }
}
