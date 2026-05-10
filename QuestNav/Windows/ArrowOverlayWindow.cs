using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using QuestNav.Services;
using System;
using System.Numerics;

namespace QuestNav.Windows
{
    /// <summary>
    /// A small transparent overlay that draws a directional arrow pointing toward
    /// the currently tracked quest's objective (issuer NPC position).
    /// When the player is in a different territory, shows a globe icon to indicate
    /// a teleport is needed instead.
    /// </summary>
    public sealed class ArrowOverlayWindow : Window, IDisposable
    {
        private readonly Configuration config;
        private readonly IClientState clientState;

        private QuestEntry? navTarget;

        // Fixed window size — user can drag but not resize
        private static readonly Vector2 WindowSize = new(110f, 130f);

        public ArrowOverlayWindow(Configuration config, IClientState clientState)
            : base("##questnav_arrow",
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav |
                ImGuiWindowFlags.NoBackground)
        {
            this.config = config;
            this.clientState = clientState;

            Size = WindowSize;
            SizeCondition = ImGuiCond.Always;
            IsOpen = false; // Start closed; will be opened when nav target is set
        }

        public void SetNavTarget(QuestEntry? entry)
        {
            navTarget = entry;
            IsOpen = entry != null;
        }

        public void Dispose() { }

        public override void PreDraw()
        {
            // Close if arrow display disabled or logged out; SetNavTarget handles opening
            if (!config.ShowArrow || !clientState.IsLoggedIn)
                IsOpen = false;
        }

        public override unsafe void Draw()
        {
            if (navTarget == null) return;
            var player = Control.GetLocalPlayer();
            if (player == null) return;

            var winPos  = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();

            // Center of the arrow circle: upper 80% of the window
            var center = winPos + new Vector2(winSize.X * 0.5f, winSize.Y * 0.45f);
            const float Radius = 34f;

            // Semi-transparent dark disc as background (opacity controlled by config)
            var bgColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, config.ArrowBgOpacity));
            drawList.AddCircleFilled(center, Radius + 5f, bgColor);

            bool sameZone = clientState.TerritoryType == (ushort)navTarget.TerritoryId;

            if (sameZone)
            {
                float dx = navTarget.WorldX - player->Position.X;
                float dz = navTarget.WorldZ - player->Position.Z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);

                // World bearing relative to north (0 = north, π/2 = east)
                // FFXIV: Z increases southward, X increases eastward
                double worldBearing = Math.Atan2(dx, -dz);
                // Subtract player rotation (0 = north in FFXIV) to get screen-relative bearing
                float angle = (float)(worldBearing - player->Rotation);

                // Arrow color: gold when in range (<30y), green otherwise
                var arrowCol = dist < 30f
                    ? new Vector4(0.2f, 1f, 0.3f, 1f)
                    : new Vector4(1f, 0.85f, 0.1f, 1f);
                var arrowColor = ImGui.ColorConvertFloat4ToU32(arrowCol);

                // Draw arrow as a filled triangle
                var tip   = center + new Vector2( MathF.Sin(angle),        -MathF.Cos(angle))        * Radius;
                var base1 = center + new Vector2( MathF.Sin(angle + 2.45f), -MathF.Cos(angle + 2.45f)) * (Radius * 0.42f);
                var base2 = center + new Vector2( MathF.Sin(angle - 2.45f), -MathF.Cos(angle - 2.45f)) * (Radius * 0.42f);
                drawList.AddTriangleFilled(tip, base1, base2, arrowColor);

                // Small dot at center
                drawList.AddCircleFilled(center, 3f, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.6f)));

                // Distance label at bottom
                var distText = dist < 1000f ? $"{dist:F0} y" : $"{dist / 1000f:F1} km";
                var textW = ImGui.CalcTextSize(distText).X;
                ImGui.SetCursorPos(new Vector2((winSize.X - textW) * 0.5f, winSize.Y - 22f));
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.9f), distText);
            }
            else
            {
                // Different zone: draw globe icon to indicate "teleport needed"
                var blue = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.75f, 1f, 1f));
                drawList.AddCircle(center, Radius * 0.72f, blue, 32, 2f);
                drawList.AddLine(
                    center - new Vector2(Radius * 0.72f, 0),
                    center + new Vector2(Radius * 0.72f, 0), blue, 1.5f);
                drawList.AddLine(
                    center - new Vector2(0, Radius * 0.72f),
                    center + new Vector2(0, Radius * 0.72f), blue, 1.5f);

                const string label = "Teleport";
                var textW = ImGui.CalcTextSize(label).X;
                ImGui.SetCursorPos(new Vector2((winSize.X - textW) * 0.5f, winSize.Y - 22f));
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 0.9f), label);
            }

            // Quest name (truncated) at very top, centred
            var display = navTarget.Name.Length > 14 ? navTarget.Name[..13] + "…" : navTarget.Name;
            var nameW = ImGui.CalcTextSize(display).X;
            ImGui.SetCursorPos(new Vector2((winSize.X - nameW) * 0.5f, 3f));
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 0.75f), display);
        }
    }
}
