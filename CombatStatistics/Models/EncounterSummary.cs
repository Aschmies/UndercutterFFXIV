using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatStatistics.Models;

public sealed class EncounterSummary
{
    private readonly Dictionary<string, ActorStats> actors = new();

    public DateTime StartedUtc { get; private set; } = DateTime.MinValue;
    public DateTime LastEventUtc { get; private set; } = DateTime.MinValue;
    public DateTime? EndedUtc { get; private set; }
    public int EventCount { get; private set; }
    public bool IsActive => StartedUtc != DateTime.MinValue && EndedUtc == null;

    public IReadOnlyDictionary<string, ActorStats> Actors => actors;

    public void BeginIfNeeded(DateTime timestampUtc)
    {
        if (StartedUtc != DateTime.MinValue)
            return;

        StartedUtc = timestampUtc;
        LastEventUtc = timestampUtc;
    }

    public void RecordEvent(CombatEvent combatEvent)
    {
        BeginIfNeeded(combatEvent.TimestampUtc);
        LastEventUtc = combatEvent.TimestampUtc;
        EventCount++;

        var actor = GetOrCreateActor(combatEvent.Source);
        actor.Record(combatEvent);
    }

    public ActorStats GetOrCreateActor(ActorIdentity actorIdentity)
    {
        var key = actorIdentity.ActorKey;
        if (!actors.TryGetValue(key, out var stats))
        {
            stats = new ActorStats(actorIdentity);
            actors[key] = stats;
        }
        return stats;
    }

    public void End(DateTime timestampUtc)
    {
        if (StartedUtc == DateTime.MinValue)
            return;

        EndedUtc = timestampUtc;
    }

    public void Clear()
    {
        actors.Clear();
        StartedUtc = DateTime.MinValue;
        LastEventUtc = DateTime.MinValue;
        EndedUtc = null;
        EventCount = 0;
    }

    public TimeSpan Duration =>
        StartedUtc == DateTime.MinValue || LastEventUtc == DateTime.MinValue
            ? TimeSpan.Zero
            : ((EndedUtc ?? LastEventUtc) - StartedUtc);

    public long TotalRaidDamage => actors.Values.Sum(actor => actor.DamageTotal);
    public long TotalRaidHealing => actors.Values.Sum(actor => actor.HealingTotal);
    public IReadOnlyList<ActorStats> TopDamageActors(int maxRows) => actors.Values.OrderByDescending(x => x.DamageTotal).Take(maxRows).ToList();
}
