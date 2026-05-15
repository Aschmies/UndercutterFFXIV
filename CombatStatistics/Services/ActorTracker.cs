using System;
using System.Collections.Generic;
using System.Linq;
using CombatStatistics.Models;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;

namespace CombatStatistics.Services;

public sealed class ActorTracker
{
    private readonly Dictionary<uint, ActorIdentity> actorsByObjectId = new();
    private readonly Dictionary<ulong, ActorIdentity> actorsByContentId = new();
    private readonly Dictionary<string, List<ActorIdentity>> actorsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, uint> petOwners = new();

    public void Reset()
    {
        actorsByObjectId.Clear();
        actorsByContentId.Clear();
        actorsByName.Clear();
        petOwners.Clear();
    }

    public void RefreshWorldSnapshot(IObjectTable objectTable, IPartyList partyList, IClientState clientState)
    {
        if (objectTable.LocalPlayer is IPlayerCharacter localPlayer)
            Upsert(ActorIdentity.FromObject(localPlayer, ActorType.Player, ResolveJobName(localPlayer, ActorType.Player)));

        foreach (dynamic partyMember in partyList)
        {
            var memberActor = new ActorIdentity
            {
                ObjectId = partyMember.ObjectId,
                ContentId = partyMember.ContentId,
                Name = partyMember.Name.ToString(),
                Job = ResolvePartyJobName(partyMember),
                Type = ActorType.Player,
            };
            Upsert(memberActor);
        }

        foreach (var obj in objectTable)
        {
            if (obj == null || obj.GameObjectId == 0)
                continue;

            var type = DetermineActorType(obj);
            var actor = ActorIdentity.FromObject(obj, type, ResolveJobName(obj, type));
            Upsert(actor);
        }

        foreach (var pair in petOwners.ToArray())
        {
            if (actorsByObjectId.TryGetValue(pair.Key, out var pet) && actorsByObjectId.TryGetValue(pair.Value, out var owner))
                pet.OwnerObjectId = owner.ObjectId;
        }
    }

    public void RegisterPetOwnership(uint petObjectId, uint ownerObjectId)
    {
        petOwners[petObjectId] = ownerObjectId;
        if (actorsByObjectId.TryGetValue(petObjectId, out var pet))
            pet.OwnerObjectId = ownerObjectId;
    }

    public ActorIdentity NormalizeForMerge(ActorIdentity actor, bool mergePets)
    {
        if (!mergePets || !actor.IsPet || actor.OwnerObjectId is null)
            return actor;

        if (actorsByObjectId.TryGetValue(actor.OwnerObjectId.Value, out var owner))
            return owner;

        return actor;
    }

    public ActorIdentity Upsert(ActorIdentity actor)
    {
        actorsByObjectId[actor.ObjectId] = actor;

        if (actor.ContentId is { } contentId)
            actorsByContentId[contentId] = actor;

        var nameKey = actor.Name.Trim();
        if (!string.IsNullOrWhiteSpace(nameKey))
        {
            if (!actorsByName.TryGetValue(nameKey, out var actors))
            {
                actors = new List<ActorIdentity>();
                actorsByName[nameKey] = actors;
            }

            if (!actors.Any(existing => existing.ActorKey == actor.ActorKey))
                actors.Add(actor);
        }

        return actor;
    }

    public bool TryGetByObjectId(uint objectId, out ActorIdentity? actor)
        => actorsByObjectId.TryGetValue(objectId, out actor);

    public bool TryGetByContentId(ulong contentId, out ActorIdentity? actor)
        => actorsByContentId.TryGetValue(contentId, out actor);

    public ActorIdentity ResolveFromLogEntity(ILogMessageEntity? entity, ActorType fallbackType, bool preferPartyActor = false)
    {
        if (entity == null)
            return new ActorIdentity { Name = string.Empty, Type = ActorType.Unknown };

        var name = entity.Name.ExtractText().Trim();
        if (!string.IsNullOrWhiteSpace(name) && actorsByName.TryGetValue(name, out var matches))
        {
            var resolved = matches
                .OrderByDescending(actor => actor.Type == ActorType.Player)
                .ThenByDescending(actor => preferPartyActor && actor.Type != ActorType.Enemy)
                .FirstOrDefault();

            if (resolved != null)
                return resolved;
        }

        var type = entity.IsPlayer ? ActorType.Player : fallbackType;
        return ActorIdentity.FromLogEntity(entity, type);
    }

    private static ActorType DetermineActorType(IGameObject obj)
    {
        if (obj is IPlayerCharacter)
            return ActorType.Player;

        var kindName = obj.ObjectKind.ToString();
        if (kindName.Contains("Pet", StringComparison.OrdinalIgnoreCase) || kindName.Contains("Companion", StringComparison.OrdinalIgnoreCase))
            return ActorType.Pet;

        if (kindName.Contains("Battle", StringComparison.OrdinalIgnoreCase) || kindName.Contains("Enemy", StringComparison.OrdinalIgnoreCase) || kindName.Contains("Npc", StringComparison.OrdinalIgnoreCase))
            return ActorType.Enemy;

        return ActorType.Unknown;
    }

    private static string ResolveJobName(IGameObject obj, ActorType actorType)
    {
        if (actorType == ActorType.Player && obj is IPlayerCharacter player)
            return player.ClassJob.RowId.ToString();

        return string.Empty;
    }

    private static string ResolvePartyJobName(dynamic partyMember)
    {
        try
        {
            var classJob = partyMember.ClassJob;
            return classJob.RowId.ToString();
        }
        catch
        {
        }

        return string.Empty;
    }
}
