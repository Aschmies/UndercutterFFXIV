using System;
using System.Numerics;
using MinimapPlugin.Models;

namespace MinimapPlugin.Helpers;

public static class CoordinateHelper
{
    /// <summary>
    /// Converts world-space (X, Z) coordinates to map-pixel coordinates using Lumina map metadata.
    /// Verified FFXIV formula: displayX = 21 + (worldX + offsetX) * sf / 2048
    ///                          displayY = 21 - (worldZ + offsetY) * sf / 2048   (Z inverted: +Z = south = top of display)
    /// → pixelX = (worldX + offsetX) * (sf / 40) + texW/2
    ///   pixelY = -(worldZ + offsetY) * (sf / 40) + texH/2
    /// </summary>
    public static Vector2 WorldToMapPixel(float worldX, float worldZ, ZoneMapInfo map, int textureWidth, int textureHeight)
    {
        float scale = map.SizeFactor / 40.0f;
        float mapX =  (worldX + map.OffsetX) * scale + (textureWidth  * 0.5f);
        float mapY = -(worldZ + map.OffsetY) * scale + (textureHeight * 0.5f);
        return new Vector2(mapX, mapY);
    }

    /// <summary>
    /// Projects a map-pixel position to minimap screen coordinates relative to the player's position at window centre.
    /// </summary>
    /// <param name="markerMapPixel">Marker position in map-pixel space.</param>
    /// <param name="playerMapPixel">Player position in map-pixel space (maps to window centre).</param>
    /// <param name="windowCenter">Centre of the minimap window in screen space.</param>
    /// <param name="visibleHalfSizePixels">Half the visible map-pixel radius (controls zoom).</param>
    /// <param name="windowHalfSize">Half the window size in screen pixels.</param>
    public static Vector2 MapPixelToScreen(
        Vector2 markerMapPixel,
        Vector2 playerMapPixel,
        Vector2 windowCenter,
        float visibleHalfSizePixels,
        float windowHalfSize)
    {
        float scale = windowHalfSize / visibleHalfSizePixels;
        return new Vector2(
            windowCenter.X + (markerMapPixel.X - playerMapPixel.X) * scale,
            windowCenter.Y + (markerMapPixel.Y - playerMapPixel.Y) * scale);
    }

    /// <summary>
    /// Projects a map-pixel position to minimap screen coordinates with rotation applied (for player-up mode).
    /// The offset from the player is rotated by <paramref name="rotation"/> before scaling.
    /// </summary>
    public static Vector2 MapPixelToScreenRotated(
        Vector2 markerMapPixel,
        Vector2 playerMapPixel,
        Vector2 windowCenter,
        float visibleHalfSizePixels,
        float windowHalfSize,
        float rotation)
    {
        float scale = windowHalfSize / visibleHalfSizePixels;
        float dx = (markerMapPixel.X - playerMapPixel.X) * scale;
        float dy = (markerMapPixel.Y - playerMapPixel.Y) * scale;

        float cos = MathF.Cos(-rotation);
        float sin = MathF.Sin(-rotation);

        return new Vector2(
            windowCenter.X + dx * cos - dy * sin,
            windowCenter.Y + dx * sin + dy * cos);
    }
}
