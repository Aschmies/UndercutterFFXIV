using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;
using UndercutterFFXIV.Services;

namespace UndercutterFFXIV.Windows
{
    public class MarketBoardWindow : Window, IDisposable
    {
        private MarketAssistantPlugin plugin { get; }
        private MarketTracker tracker { get; }
        private NotificationService notifications { get; }

        public MarketBoardWindow(MarketAssistantPlugin plugin, MarketTracker tracker, NotificationService notifications) 
            : base("Market Board###MarketBoardWindow")
        {
            this.plugin = plugin;
            this.tracker = tracker;
            this.notifications = notifications;
            Size = new Vector2(900, 600);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            ImGui.TextWrapped("Market Board - Tracked Items");
            ImGui.Separator();
            ImGui.TextWrapped("Use /ma flip to see flipping opportunities");
            ImGui.TextWrapped("Use /ma tracker to see flip statistics");
        }

        public void Dispose() { }
    }
}
