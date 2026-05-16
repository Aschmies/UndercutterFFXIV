using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using MinimapPlugin.Models;

namespace MinimapPlugin.Services;

public sealed class EntityService
{
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IClientState clientState;
    private readonly IPluginLog log;

    public EntityService(IObjectTable objectTable, IPartyList partyList, IClientState clientState, IPluginLog log)
    {
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.clientState = clientState;
        this.log = log;
    }

    /// <summary>Collects all entity markers for the current frame based on the provided configuration.</summary>
    public List<MapMarker> GetMarkers(Configuration config)
    {
        var markers = new List<MapMarker>();

        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer == null) return markers;

        // Build a set of party member game-object IDs for O(1) lookup
        var partyGameObjectIds = new HashSet<ulong>();
        foreach (var member in partyList)
        {
            if (member.GameObject != null)
                partyGameObjectIds.Add(member.GameObject.GameObjectId);
        }

        foreach (var obj in objectTable)
        {
            if (obj == null) continue;

            // Skip the local player — they are always drawn as the centred arrow
            if (obj.GameObjectId == localPlayer.GameObjectId) continue;

            var kind = obj.ObjectKind;
            var pos  = obj.Position;
            MarkerType markerType;

            // Player characters: check via type rather than ObjectKind.Pc
            // (IPlayerCharacter is the reliable way to identify PCs)
            if (obj is IPlayerCharacter)
            {
                bool isParty = partyGameObjectIds.Contains(obj.GameObjectId);
                if (isParty && config.ShowPartyMembers)
                    markerType = MarkerType.PartyMember;
                else if (!isParty && config.ShowOtherPlayers)
                    markerType = MarkerType.OtherPlayer;
                else
                    continue;
            }
            else
            {
                switch (kind)
                {
                    case ObjectKind.Aetheryte:
                        if (!config.ShowAetherytes) continue;
                        markerType = MarkerType.Aetheryte;
                        break;

                    case ObjectKind.EventObj:
                        if (!config.ShowFates) continue;
                        markerType = MarkerType.Fate;
                        break;

                    default:
                        continue;
                }
            }

            markers.Add(new MapMarker
            {
                WorldX = pos.X,
                WorldZ = pos.Z,
                Type   = markerType,
                Label  = obj.Name.TextValue,
            });
        }

        return markers;
    }
}
