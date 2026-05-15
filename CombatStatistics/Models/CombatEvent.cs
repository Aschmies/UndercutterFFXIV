using System;

namespace CombatStatistics.Models;

public sealed class CombatEvent
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public CombatEventType Type { get; init; }
    public ActorIdentity Source { get; init; } = new();
    public ActorIdentity Target { get; init; } = new();
    public uint ActionId { get; init; }
    public int Amount { get; init; }
    public int Overheal { get; init; }
    public bool IsCritical { get; init; }
    public bool IsDirectHit { get; init; }
    public bool IsPet { get; init; }
    public bool IsDamageOverTime { get; init; }
    public bool IsHealingOverTime { get; init; }

    public int NetHealing => Math.Max(0, Amount - Overheal);
}
