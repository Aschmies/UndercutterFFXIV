using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace CombatStatistics.Models;

public sealed class ActorIdentity
{
    public uint ObjectId { get; init; }
    public ulong? ContentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Job { get; init; } = string.Empty;
    public ActorType Type { get; init; } = ActorType.Unknown;
    public uint? OwnerObjectId { get; set; }

    public bool IsPet => Type == ActorType.Pet;
    public bool IsPlayer => Type == ActorType.Player;

    public string DisplayName => string.IsNullOrWhiteSpace(Job) ? Name : $"{Name} ({Job})";

    public string ActorKey => ContentId?.ToString() ?? ObjectId.ToString();

    public static ActorIdentity FromObject(IGameObject obj, ActorType type, string job = "", uint? ownerObjectId = null)
    {
        return new ActorIdentity
        {
            ObjectId = unchecked((uint)obj.GameObjectId),
            ContentId = null,
            Name = obj.Name.ToString(),
            Job = job,
            Type = type,
            OwnerObjectId = ownerObjectId,
        };
    }
}
