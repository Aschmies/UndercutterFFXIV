using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace QuestNav.Services
{
    /// <summary>
    /// Represents a single step within a quest.
    /// </summary>
    public record QuestStep(
        uint StepIndex,
        string Title,
        string Description,
        string Objectives,
        uint? NpcTerritoryId,
        uint? NpcMapId,
        float? NpcWorldX,
        float? NpcWorldZ,
        float? NpcMapX,
        float? NpcMapY
    );

    /// <summary>
    /// Complete quest information including all steps and current progress.
    /// </summary>
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
        byte Sequence,       // current quest step sequence number
        List<QuestStep> AllSteps  // all quest steps
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

                var questRow = questSheet.GetRowOrDefault(questId);
                if (questRow == null || questRow.Value.RowId == 0)
                    questRow = questSheet.GetRowOrDefault((uint)(questId | 0x10000));
                if (questRow == null || questRow.Value.RowId == 0) continue;
                var quest = questRow.Value;

                var name = quest.Name.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var sequence = QuestManager.GetQuestSequence(questId);

                // Quest type from JournalGenre
                var questType = string.Empty;
                try
                {
                    var genre = quest.JournalGenre.Value;
                    if (genre.RowId != 0)
                        questType = genre.Name.ToString();
                }
                catch { }

                // Resolve location + zone from IssuerLocation Level row
                var zoneName = "Unknown Zone";
                uint territoryId = 0;
                uint mapId = 0;
                float worldX = 0f, worldZ = 0f, mapX = 0f, mapY = 0f;

                var levelRowId = quest.IssuerLocation.RowId;
                if (levelRowId != 0)
                {
                    var levelRow = quest.IssuerLocation.Value;
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

                // Retrieve all quest steps
                var allSteps = GetQuestSteps(questId, quest);

                results.Add(new QuestEntry(
                    questId, name, zoneName,
                    territoryId, mapId, worldX, worldZ, mapX, mapY,
                    aetheryteId, aetheryteName,
                    gilCost, questType, sequence, allSteps));
            }

            return results;
        }

        /// <summary>
        /// Retrieves all steps for a quest from the Lumina sheets.
        /// Quest step information comes from the Quest sheet entries indexed by questId + step offset.
        /// </summary>
        private List<QuestStep> GetQuestSteps(ushort questId, Quest quest)
        {
            var steps = new List<QuestStep>();

            try
            {
                var questSheet = dataManager.GetExcelSheet<Quest>();
                if (questSheet == null) return steps;

                // In FFXIV Lumina, quest steps are stored as sequential entries
                // Each quest has multiple sheet rows (one per step, indexed by quest ID)
                for (uint stepIdx = 0; stepIdx < 100; stepIdx++)  // Max reasonable quest steps
                {
                    // Try to get the quest row for this step
                    var stepQuestId = (uint)questId + (stepIdx << 16);
                    var stepRow = questSheet.GetRowOrDefault(stepQuestId);
                    
                    if (stepRow == null) break;
                    
                    var qData = stepRow.Value;
                    if (qData.RowId == 0) break;
                    
                    var title = qData.Name.ToString();
                    // For steps, description and objectives would need to come from related sheets
                    // which may not have direct Lumina bindings yet
                    var description = $"Quest Step {stepIdx}";
                    var objectives = "Objectives unavailable";
                    
                    // Get the target location for this step from IssuerLocation
                    uint? npcTerritory = null;
                    uint? npcMapId = null;
                    float? npcWorldX = null;
                    float? npcWorldZ = null;
                    float? npcMapX = null;
                    float? npcMapY = null;

                    try
                    {
                        var targetLocRowId = qData.IssuerLocation.RowId;
                        if (targetLocRowId != 0)
                        {
                            var targetLoc = qData.IssuerLocation.Value;
                            npcTerritory = targetLoc.Territory.RowId;
                            if (npcTerritory != 0)
                            {
                                npcWorldX = targetLoc.X;
                                npcWorldZ = targetLoc.Z;
                                
                                var mapRow = targetLoc.Territory.Value.Map.Value;
                                npcMapId = mapRow.RowId;
                                (npcMapX, npcMapY) = WorldToMapCoord(npcWorldX.Value, npcWorldZ.Value, mapRow);
                            }
                        }
                    }
                    catch { }

                    steps.Add(new QuestStep(
                        stepIdx,
                        title,
                        description,
                        objectives,
                        npcTerritory,
                        npcMapId,
                        npcWorldX,
                        npcWorldZ,
                        npcMapX,
                        npcMapY
                    ));
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"[QuestNav] Failed to retrieve steps for quest {questId}");
            }

            return steps;
        }

        /// <summary>
        /// Retrieves ALL quests from the game data (not just active ones).
        /// Useful for accessing complete quest information and step data.
        /// </summary>
        public List<QuestEntry> GetAllQuests()
        {
            var results = new List<QuestEntry>();

            try
            {
                var questSheet = dataManager.GetExcelSheet<Quest>();
                if (questSheet == null) return results;

                // Iterate through all rows in the quest sheet (up to a reasonable max)
                // Note: Each quest can have multiple entries for different steps
                var processedQuests = new HashSet<ushort>();

                for (uint rowId = 0; rowId < 10000; rowId++)
                {
                    var questRow = questSheet.GetRowOrDefault(rowId);
                    if (questRow == null || questRow.Value.RowId == 0) continue;
                    
                    var quest = questRow.Value;
                    var questId = (ushort)(rowId & 0xFFFF);
                    
                    // Skip if we've already processed this quest (since steps have same base ID)
                    if (processedQuests.Contains(questId)) continue;
                    processedQuests.Add(questId);
                    
                    var name = quest.Name.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Quest type from JournalGenre
                    var questType = string.Empty;
                    try
                    {
                        var genre = quest.JournalGenre.Value;
                        if (genre.RowId != 0)
                            questType = genre.Name.ToString();
                    }
                    catch { }

                    // Resolve location + zone from IssuerLocation
                    var zoneName = "Unknown Zone";
                    uint territoryId = 0;
                    uint mapId = 0;
                    float worldX = 0f, worldZ = 0f, mapX = 0f, mapY = 0f;

                    var levelRowId = quest.IssuerLocation.RowId;
                    if (levelRowId != 0)
                    {
                        var levelRow = quest.IssuerLocation.Value;
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

                    // Find nearest aetheryte
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

                    // Get all steps for this quest
                    var allSteps = GetQuestSteps(questId, quest);

                    results.Add(new QuestEntry(
                        questId, name, zoneName,
                        territoryId, mapId, worldX, worldZ, mapX, mapY,
                        aetheryteId, aetheryteName,
                        gilCost, questType, 0, allSteps));  // sequence = 0 for non-active quests
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, "[QuestNav] Failed to retrieve all quests");
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
