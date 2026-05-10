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
        uint? NearestAetheryteId,
        string AetheryteName
    );

    public sealed unsafe class QuestService(
        IDataManager dataManager,
        IAetheryteList aetheryteList,
        IPluginLog log)
    {
        /// <summary>
        /// Returns all quests currently active in the player's NormalQuests slots.
        /// Location data is based on the quest issuer's position (the NPC who gave the quest).
        /// This is usually where you turn in the quest and a good approximation for navigation.
        /// </summary>
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

                // Try the direct ID first, then with the common 0x10000 offset
                var questRow = questSheet.GetRow(questId);
                if (questRow.RowId == 0)
                    questRow = questSheet.GetRow((uint)(questId | 0x10000));
                if (questRow.RowId == 0) continue;

                var name = questRow.Name.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Resolve location from the issuer's Level row
                var zoneName = "Unknown Zone";
                uint? aetheryteId = null;
                var aetheryteName = "No aetheryte nearby";

                var levelRowId = questRow.IssuerLocation.RowId;
                if (levelRowId != 0)
                {
                    var levelRow = questRow.IssuerLocation.Value;
                    var territoryId = levelRow.Territory.RowId;
                    if (territoryId != 0)
                    {
                        var placeName = levelRow.Territory.Value.PlaceName.Value.Name.ToString();
                        zoneName = string.IsNullOrWhiteSpace(placeName) ? $"Zone {territoryId}" : placeName;

                        var (id, aName) = FindAetheryte(territoryId);
                        if (id.HasValue)
                        {
                            aetheryteId = id;
                            aetheryteName = aName!;
                        }
                    }
                }

                results.Add(new QuestEntry(questId, name, zoneName, aetheryteId, aetheryteName));
            }

            return results;
        }

        /// <summary>
        /// Teleports to the given aetheryte via Telepo. Returns false if the game rejects it.
        /// </summary>
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

        /// <summary>
        /// Returns the first unlocked main aetheryte (not aethernet shard/estate) in the given territory.
        /// </summary>
        private (uint? Id, string? Name) FindAetheryte(uint territoryId)
        {
            foreach (var entry in aetheryteList)
            {
                if (entry.TerritoryId != territoryId) continue;
                if (entry.IsApartment || entry.IsSharedHouse) continue;

                var aetheryteRow = entry.AetheryteData.Value;
                if (aetheryteRow.RowId == 0) continue;

                // IsAetheryte == true means it's a main teleport point, not an aethernet shard
                if (!aetheryteRow.IsAetheryte) continue;

                var name = aetheryteRow.PlaceName.Value.Name.ToString();
                return (entry.AetheryteId, string.IsNullOrWhiteSpace(name) ? "Aetheryte" : name);
            }
            return (null, null);
        }
    }
}
