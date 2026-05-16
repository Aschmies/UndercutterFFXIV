using System;
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

    public bool IsLoading { get; private set; }
    public ZoneMapInfo? CurrentMapInfo { get; private set; }

    public MapDataService(IDataManager dataManager, ITextureProvider textureProvider, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        this.log = log;
    }

    public void LoadMapForTerritory(uint territoryId)
    {
        if (territoryId == 0) return;

        IsLoading = true;

        // Clear previous state immediately so the window shows "loading"
        currentTexture = null;
        CurrentMapInfo = null;

        Task.Run(() =>
        {
            try
            {
                var territorySheet = dataManager.GetExcelSheet<TerritoryType>();
                if (territorySheet == null)
                {
                    log.Warning("[Minimap] TerritoryType sheet not available.");
                    return;
                }

                var territory = territorySheet.GetRowOrDefault(territoryId);
                if (territory == null)
                {
                    log.Warning($"[Minimap] No TerritoryType row for id {territoryId}.");
                    return;
                }

                var map = territory.Value.Map.Value;
                var mapId = map.Id.ExtractText().TrimEnd('\0');

                if (string.IsNullOrEmpty(mapId))
                {
                    log.Warning($"[Minimap] Empty map ID for territory {territoryId}.");
                    return;
                }

                var texPath = $"ui/map/{mapId}_m.tex";

                var info = new ZoneMapInfo
                {
                    TerritoryId = territoryId,
                    MapId = mapId,
                    TexturePath = texPath,
                    SizeFactor = map.SizeFactor,
                    OffsetX = map.OffsetX,
                    OffsetY = map.OffsetY,
                };

                var texture = textureProvider.GetFromGame(texPath);

                CurrentMapInfo = info;
                currentTexture = texture;
                log.Debug($"[Minimap] Loaded map {mapId} for territory {territoryId}.");
            }
            catch (Exception ex)
            {
                log.Error(ex, $"[Minimap] Failed to load map for territory {territoryId}.");
            }
            finally
            {
                IsLoading = false;
            }
        });
    }

    /// <summary>Returns the current texture wrap for use within an ImGui Draw frame, or null if unavailable.</summary>
    /// <summary>Returns the current texture wrap for the current frame, or null if unavailable. Must be called from the main thread.</summary>
    public Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? GetCurrentTextureWrap()
        => currentTexture?.GetWrapOrEmpty();

    public void Dispose()
    {
        // ISharedImmediateTexture is not IDisposable — the texture is managed by Dalamud's cache.
        currentTexture = null;
    }
}
