using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using MinimapPlugin.Models;
using Lumina.Excel.Sheets;

namespace MinimapPlugin.Services;

public sealed class MapDataService : IDisposable
{
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private readonly IPluginLog log;

    private ISharedImmediateTexture? currentTexture;
    // Fallback paths to try automatically if the loaded texture is a tiny stub (≤16 px)
    private readonly Queue<string> texFallbackQueue = new();
    private bool texDimensionsLogged;

    public bool IsLoading { get; private set; }
    public ZoneMapInfo? CurrentMapInfo { get; private set; }

    public MapDataService(IDataManager dataManager, ITextureProvider textureProvider, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        this.log = log;
    }

    /// <summary>Load map texture using a Lumina Map row ID (from IClientState.MapId).
    /// <paramref name="territoryType"/> is optional; used to widen the fallback texture search.</summary>
    public void LoadMapForMapId(uint mapRowId, uint territoryType = 0)
    {
        if (mapRowId == 0) return;

        IsLoading = true;
        currentTexture = null;
        CurrentMapInfo = null;
        texDimensionsLogged = false;
        texFallbackQueue.Clear();

        Task.Run(() =>
        {
            try
            {
                var mapSheet = dataManager.GetExcelSheet<Map>();
                if (mapSheet == null)
                {
                    log.Warning("[Minimap] Map sheet not available.");
                    return;
                }

                var mapRow = mapSheet.GetRowOrDefault(mapRowId);
                if (mapRow == null)
                {
                    log.Warning($"[Minimap] No Map row for id {mapRowId}.");
                    return;
                }

                var map = mapRow.Value;
                var mapId = map.Id.ExtractText().TrimEnd('\0');
                var sizeFactor = map.SizeFactor == 0 ? (ushort)100 : map.SizeFactor;
                var offsetX = map.OffsetX;
                var offsetY = map.OffsetY;

                if (string.IsNullOrEmpty(mapId))
                {
                    log.Warning($"[Minimap] Empty map ID for map row {mapRowId}.");
                    return;
                }

                // Build a priority-ordered candidate list.
                // FFXIV map texture canonical format: ui/map/{territory}/{variant}/{territory}_{variant}_m.tex
                // e.g. mapId "x6fe/01" → ui/map/x6fe/01/x6fe_01_m.tex
                // Also try flat forms in case they exist as alternates/fallbacks.
                var candidates = new List<string>();
                if (mapId.Contains('/'))
                {
                    var parts  = mapId.Split('/');
                    var folder  = parts[0]; // e.g. "x6fe"
                    var variant = parts[1]; // e.g. "01"
                    // canonical sub-directory form (most zones)
                    candidates.Add($"ui/map/{folder}/{variant}/{folder}_{variant}_m.tex");
                    candidates.Add($"ui/map/{folder}/{variant}/{folder}_{variant}_s.tex");
                    // parent-zone flat form (fallback — some zones share the territory texture)
                    candidates.Add($"ui/map/{folder}/{folder}_m.tex");
                    // legacy flat form seen in older zones
                    candidates.Add($"ui/map/{folder}/{variant}_m.tex");
                }
                else
                {
                    candidates.Add($"ui/map/{mapId}/{mapId}_m.tex");   // canonical single-area form
                    candidates.Add($"ui/map/{mapId}_m.tex");            // flat form
                    candidates.Add($"ui/map/{mapId}/{mapId}_s.tex");
                    candidates.Add($"ui/map/{mapId}_s.tex");
                }

                // Add the territory's own map as a last-resort fallback
                if (territoryType != 0)
                {
                    var terr = dataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryType);
                    if (terr != null)
                    {
                        var terrMapId = terr.Value.Map.Value.Id.ExtractText().TrimEnd('\0');
                        if (!string.IsNullOrEmpty(terrMapId))
                        {
                            var tp = $"ui/map/{terrMapId}_m.tex";
                            if (!candidates.Contains(tp)) candidates.Add(tp);
                        }
                    }
                }

                // Log what exists so the Dalamud log shows the full picture
                foreach (var c in candidates)
                    log.Info($"[Minimap]   {(dataManager.FileExists(c) ? " exists" : "MISSING")} {c}");

                // Pick the first existing path; queue the rest as automatic fallbacks
                string? texPath = null;
                foreach (var c in candidates)
                {
                    if (!dataManager.FileExists(c)) continue;
                    if (texPath == null) texPath = c;
                    else texFallbackQueue.Enqueue(c);
                }
                texPath ??= candidates[0]; // last resort: attempt even if missing

                log.Info($"[Minimap] row={mapRowId} id='{mapId}' sf={sizeFactor} ox={offsetX} oy={offsetY}");
                log.Info($"[Minimap] Loading: '{texPath}' (+{texFallbackQueue.Count} fallback(s))");

                var info = new ZoneMapInfo
                {
                    MapRowId = mapRowId,
                    MapId = mapId,
                    TexturePath = texPath,
                    SizeFactor = sizeFactor,
                    OffsetX = offsetX,
                    OffsetY = offsetY,
                };

                CurrentMapInfo = info;
                currentTexture = textureProvider.GetFromGame(texPath);
            }
            catch (Exception ex)
            {
                log.Error(ex, $"[Minimap] Failed to load map for map row {mapRowId}.");
            }
            finally
            {
                IsLoading = false;
            }
        });
    }

    /// <summary>Returns the current texture wrap for use within an ImGui Draw frame.
    /// Returns null while the texture is still loading or while a stub fallback is being swapped in.</summary>
    public Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? GetCurrentTextureWrap()
    {
        var wrap = currentTexture?.GetWrapOrEmpty();
        if (wrap == null || wrap.Width <= 1) return null;

        // Stub detection: if the loaded texture is tiny (≤16 px placeholder), try the next fallback.
        if (wrap.Width <= 16 && wrap.Height <= 16 && texFallbackQueue.Count > 0)
        {
            var nextPath = texFallbackQueue.Dequeue();
            log.Warning($"[Minimap] Texture is {wrap.Width}×{wrap.Height} stub; trying next fallback: '{nextPath}'");
            texDimensionsLogged = false;
            currentTexture = textureProvider.GetFromGame(nextPath);
            if (CurrentMapInfo != null) CurrentMapInfo.TexturePath = nextPath;
            return null; // pick up on next frame
        }

        if (!texDimensionsLogged)
        {
            texDimensionsLogged = true;
            log.Info($"[Minimap] Texture ready: {wrap.Width}×{wrap.Height} for '{CurrentMapInfo?.TexturePath}'");
        }

        return wrap;
    }

    public void Dispose()
    {
        currentTexture = null;
    }
}
