using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatStatistics.Models;

public sealed class EncounterSnapshot
{
    public DateTime StartedUtc { get; init; }
    public DateTime EndedUtc { get; init; }
    public IReadOnlyDictionary<string, ActorStats> ActorStats { get; init; } = new Dictionary<string, ActorStats>();
    public int EventCount { get; init; }

    public TimeSpan Duration => EndedUtc - StartedUtc;
    public long TotalRaidDamage => ActorStats.Values.Sum(actor => actor.DamageTotal);
    public long TotalRaidHealing => ActorStats.Values.Sum(actor => actor.HealingTotal);
}
