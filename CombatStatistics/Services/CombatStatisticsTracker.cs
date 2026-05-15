using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CombatStatistics.Config;
using CombatStatistics.Models;
using CombatStatistics.Parsing;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace CombatStatistics.Services;

public sealed class CombatStatisticsTracker
{
    private readonly Configuration configuration;
    private readonly ActorTracker actorTracker = new();
    private readonly EncounterTracker encounterTracker = new();
    private readonly CombatEventParser parser = new();
    private readonly ConcurrentQueue<CombatEvent> eventQueue = new();

    public CombatStatisticsTracker(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public bool HasActiveEncounter => encounterTracker.HasActiveEncounter;
    public EncounterSummary CurrentEncounter => encounterTracker.CurrentEncounter;
    public IReadOnlyList<EncounterSnapshot> History => encounterTracker.History;

    public void Update(IObjectTable objectTable, IPartyList partyList, IClientState clientState)
    {
        actorTracker.RefreshWorldSnapshot(objectTable, partyList, clientState);

        while (eventQueue.TryDequeue(out var combatEvent))
        {
            var source = NormalizeActor(combatEvent.Source);
            var target = NormalizeActor(combatEvent.Target);
            var normalizedEvent = new CombatEvent
            {
                TimestampUtc = combatEvent.TimestampUtc,
                Type = combatEvent.Type,
                Source = source,
                Target = target,
                ActionId = combatEvent.ActionId,
                Amount = combatEvent.Amount,
                Overheal = combatEvent.Overheal,
                IsCritical = combatEvent.IsCritical,
                IsDirectHit = combatEvent.IsDirectHit,
                IsPet = combatEvent.IsPet || source.IsPet,
                IsDamageOverTime = combatEvent.IsDamageOverTime,
                IsHealingOverTime = combatEvent.IsHealingOverTime,
            };

            if (IsRelevant(source, target, normalizedEvent.Type))
                encounterTracker.RecordEvent(normalizedEvent);
        }

        encounterTracker.Tick(TimeSpan.FromSeconds(Math.Max(1, configuration.EncounterTimeoutSeconds)));
    }

    public void HandleLogMessage(ILogMessage message)
    {
        if (!parser.TryParse(message, out var combatEvent))
            return;

        var source = NormalizeActor(combatEvent.Source);
        var target = NormalizeActor(combatEvent.Target);

        if (!IsRelevant(source, target, combatEvent.Type))
            return;

        encounterTracker.RecordEvent(new CombatEvent
        {
            TimestampUtc = combatEvent.TimestampUtc,
            Type = combatEvent.Type,
            Source = source,
            Target = target,
            ActionId = combatEvent.ActionId,
            Amount = combatEvent.Amount,
            Overheal = combatEvent.Overheal,
            IsCritical = combatEvent.IsCritical,
            IsDirectHit = combatEvent.IsDirectHit,
            IsPet = combatEvent.IsPet || source.IsPet,
            IsDamageOverTime = combatEvent.IsDamageOverTime,
            IsHealingOverTime = combatEvent.IsHealingOverTime,
        });
    }

    public void RecordDamage(ActorIdentity source, ActorIdentity target, uint actionId, int amount, bool isCritical = false, bool isDirectHit = false, bool isDoT = false, bool isPet = false)
        => eventQueue.Enqueue(parser.CreateDamageEvent(source, target, actionId, amount, isCritical, isDirectHit, isDoT, isPet));

    public void RecordHeal(ActorIdentity source, ActorIdentity target, uint actionId, int amount, int overheal = 0, bool isCritical = false, bool isDirectHit = false, bool isHoT = false, bool isPet = false)
        => eventQueue.Enqueue(parser.CreateHealEvent(source, target, actionId, amount, overheal, isCritical, isDirectHit, isHoT, isPet));

    public void RecordShield(ActorIdentity source, ActorIdentity target, uint actionId, int amount, bool isPet = false)
        => eventQueue.Enqueue(parser.CreateShieldEvent(source, target, actionId, amount, isPet));

    public void ResetEncounter() => encounterTracker.Reset();

    public void ArchiveCurrentEncounter() => encounterTracker.ArchiveCurrentEncounter();

    private ActorIdentity NormalizeActor(ActorIdentity actor)
    {
        if (configuration.MergePetsWithOwner)
            return actorTracker.NormalizeForMerge(actor, true);

        return actor;
    }

    private static bool IsRelevant(ActorIdentity source, ActorIdentity target, CombatEventType type)
    {
        if (source.Type is ActorType.Player or ActorType.Pet)
            return true;

        if (target.Type is ActorType.Player or ActorType.Pet)
            return true;

        return type is CombatEventType.Shield;
    }
}
