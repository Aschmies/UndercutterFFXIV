using System;
using System.Collections.Generic;
using System.Linq;
using CombatStatistics.Models;

namespace CombatStatistics.Services;

public sealed class EncounterTracker
{
    private readonly List<EncounterSnapshot> history = new();
    private readonly int historyLimit;

    public EncounterSummary CurrentEncounter { get; } = new();
    public IReadOnlyList<EncounterSnapshot> History => history;
    public bool HasActiveEncounter => CurrentEncounter.IsActive;

    public EncounterTracker(int historyLimit = 20)
    {
        this.historyLimit = Math.Max(1, historyLimit);
    }

    public void RecordEvent(CombatEvent combatEvent)
    {
        CurrentEncounter.RecordEvent(combatEvent);
    }

    public void Tick(TimeSpan timeout)
    {
        if (!CurrentEncounter.IsActive || CurrentEncounter.LastEventUtc == DateTime.MinValue)
            return;

        if ((DateTime.UtcNow - CurrentEncounter.LastEventUtc) < timeout)
            return;

        ArchiveCurrentEncounter();
    }

    public void Reset()
    {
        CurrentEncounter.Clear();
    }

    public void ArchiveCurrentEncounter()
    {
        if (!CurrentEncounter.IsActive)
            return;

        CurrentEncounter.End(DateTime.UtcNow);

        history.Insert(0, new EncounterSnapshot
        {
            StartedUtc = CurrentEncounter.StartedUtc,
            EndedUtc = CurrentEncounter.EndedUtc ?? DateTime.UtcNow,
            ActorStats = CurrentEncounter.Actors.ToDictionary(x => x.Key, x => x.Value),
            EventCount = CurrentEncounter.EventCount,
        });

        if (history.Count > historyLimit)
            history.RemoveRange(historyLimit, history.Count - historyLimit);

        CurrentEncounter.Clear();
    }
}
