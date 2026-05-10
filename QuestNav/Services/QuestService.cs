using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace QuestNav.Services
{
    public record QuestEntry(
        ushort QuestId,
        string Name,
        string ZoneName,
        uint TerritoryId,
        uint MapId,
        float WorldX,        // world X of issuer NPC (for arrow + map flag)
        float WorldZ,        // world Z of issuer NPC
        float MapX,          // map-space X (1–42 range) for MapLinkPayload
        float MapY,          // map-space Y (1–42 range)
        uint? NearestAetheryteId,
        string AetheryteName,
        uint GilCost,
        string QuestType,    // JournalGenre name
        byte Sequence        // current quest step sequence number
    );

    public sealed unsafe class QuestService(
        IDataManager dataManager,
        IAetheryteList aetheryteList,
        IPluginLog log)
    {
        public List<QuestEntry> GetActiveQuests()
        {
            var results = new List<QuestEntry>();

            var questSheet = dataManager.GetExcelSheet<Quest>();
            var qm = QuestManager.Instance();
            if (qm == null) return results;

            for (var i = 0; i < qm->NormalQuests.Length; i++)
            {
                var questWork = qm->NormalQuests[i];
                var questId = questWork.QuestId;
                if (questId == 0) continue;

                var questRow = questSheet.GetRow(questId);
                if (questRow.RowId == 0)
                    questRow = questSheet.GetRow((uint)(questId | 0x10000));
                if (questRow.RowId == 0) continue;

                var name = questRow.Name.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var sequence = QuestManager.GetQuestSequence(questId);

                // Quest type from JournalGenre
                var questType = string.Empty;
                try
                {
                    var genre = questRow.JournalGenre.Value;
                    if (genre.RowId != 0)
                        questType = genre.Name.ToString();
                }
                catch { }

                // Resolve location + zone from IssuerLocation Level row
                var zoneName = "Unknown Zone";
                uint territoryId = 0;
                uint mapId = 0;
                float worldX = 0f, worldZ = 0f, mapX = 0f, mapY = 0f;

                var levelRowId = questRow.IssuerLocation.RowId;
                if (levelRowId != 0)
                {
                    var levelRow = questRow.IssuerLocation.Value;
                    territoryId = levelRow.Territory.RowId;
                    if (territoryId != 0)
                    {
                        worldX = levelRow.X;
                        worldZ = levelRow.Z;

                        var territory = levelRow.Territory.Value;
                        var placeName = territory.PlaceName.Value.Name.ToString();
                        zoneName = string.IsNullOrWhiteSpace(placeName) ? $"Zone {territoryId}" : placeName;

                        var mapRow = territory.Map.Value;
                        mapId = mapRow.RowId;
                        (mapX, mapY) = WorldToMapCoord(worldX, worldZ, mapRow);
                    }
                }

                // Find nearest main aetheryte for this territory
                uint? aetheryteId = null;
                var aetheryteName = "No aetheryte nearby";
                uint gilCost = 0;

                if (territoryId != 0)
                {
                    var (id, aName) = FindAetheryte(territoryId);
                    if (id.HasValue)
                    {
                        aetheryteId = id;
                        aetheryteName = aName!;
                        gilCost = GetGilCost(id.Value);
                    }
                }

                results.Add(new QuestEntry(
                    questId, name, zoneName,
                    territoryId, mapId, worldX, worldZ, mapX, mapY,
                    aetheryteId, aetheryteName,
                    gilCost, questType, sequence));
            }

            return results;
        }

        public bool Teleport(uint aetheryteId)
        {
            try
            {
                var telepo = Telepo.Instance();
                if (telepo == null) return false;
                return telepo->Teleport(aetheryteId, 0);
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"[QuestNav] Teleport to aetheryte {aetheryteId} failed.");
                return false;
            }
        }

        private (uint? Id, string? Name) FindAetheryte(uint territoryId)
        {
            foreach (var entry in aetheryteList)
            {
                if (entry.TerritoryId != territoryId) continue;
                if (entry.IsApartment || entry.IsSharedHouse) continue;

                var aetheryteRow = entry.AetheryteData.Value;
                if (aetheryteRow.RowId == 0) continue;
                if (!aetheryteRow.IsAetheryte) continue;

                var name = aetheryteRow.PlaceName.Value.Name.ToString();
                return (entry.AetheryteId, string.IsNullOrWhiteSpace(name) ? "Aetheryte" : name);
            }
            return (null, null);
        }

        /// <summary>
        /// Looks up the GilCost for a given aetheryte from the live Telepo TeleportList.
        /// This reflects the actual cost including any discounts (free aetheryte, Inn aetheryte etc.).
        /// </summary>
        private static uint GetGilCost(uint aetheryteId)
        {
            var telepo = Telepo.Instance();
            if (telepo == null) return 0;

            foreach (var info in telepo->TeleportList.AsSpan())
            {
                if (info.AetheryteId == aetheryteId)
                    return info.GilCost;
            }
            return 0;
        }

        /// <summary>
        /// Converts FFXIV world coordinates (X, Z) to map-space coordinates (1–42 range)
        /// using the Map sheet's SizeFactor + OffsetX/Y values.
        /// </summary>
        public static (float MapX, float MapY) WorldToMapCoord(float worldX, float worldZ, Lumina.Excel.Sheets.Map mapRow)
        {
            var scale = mapRow.SizeFactor / 100f;
            var mx = 41f / scale * ((worldX + mapRow.OffsetX) / 2048f + 1f) / 2f + 1f;
            var my = 41f / scale * ((worldZ + mapRow.OffsetY) / 2048f + 1f) / 2f + 1f;
            return (mx, my);
        }
    }
}
