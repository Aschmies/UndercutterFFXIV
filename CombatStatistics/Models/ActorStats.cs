using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatStatistics.Models;

public sealed class ActorStats
{
    public ActorIdentity Actor { get; }
    public long DamageTotal { get; private set; }
    public long DoTDamageTotal { get; private set; }
    public long PetDamageTotal { get; private set; }
    public long HealingTotal { get; private set; }
    public long HoTHealingTotal { get; private set; }
    public long ShieldTotal { get; private set; }
    public long OverhealTotal { get; private set; }
    public int DamageEvents { get; private set; }
    public int HealingEvents { get; private set; }
    public DateTime FirstEventUtc { get; private set; } = DateTime.MinValue;
    public DateTime LastEventUtc { get; private set; } = DateTime.MinValue;

    public Dictionary<uint, long> DamageByAction { get; } = new();
    public Dictionary<uint, long> HealingByAction { get; } = new();
    public Dictionary<uint, long> ShieldByAction { get; } = new();

    public ActorStats(ActorIdentity actor)
    {
        Actor = actor;
    }

    public void Record(CombatEvent combatEvent)
    {
        if (FirstEventUtc == DateTime.MinValue)
            FirstEventUtc = combatEvent.TimestampUtc;

        LastEventUtc = combatEvent.TimestampUtc;

        switch (combatEvent.Type)
        {
            case CombatEventType.Damage:
                DamageTotal += combatEvent.Amount;
                DamageEvents++;
                DamageByAction.TryGetValue(combatEvent.ActionId, out var damage);
                DamageByAction[combatEvent.ActionId] = damage + combatEvent.Amount;
                if (combatEvent.IsDamageOverTime)
                    DoTDamageTotal += combatEvent.Amount;
                if (combatEvent.IsPet)
                    PetDamageTotal += combatEvent.Amount;
                break;
            case CombatEventType.DoTTick:
                DamageTotal += combatEvent.Amount;
                DoTDamageTotal += combatEvent.Amount;
                DamageEvents++;
                DamageByAction.TryGetValue(combatEvent.ActionId, out var dotDamage);
                DamageByAction[combatEvent.ActionId] = dotDamage + combatEvent.Amount;
                if (combatEvent.IsPet)
                    PetDamageTotal += combatEvent.Amount;
                break;
            case CombatEventType.Heal:
                HealingTotal += combatEvent.Amount;
                OverhealTotal += combatEvent.Overheal;
                HealingEvents++;
                HealingByAction.TryGetValue(combatEvent.ActionId, out var heal);
                HealingByAction[combatEvent.ActionId] = heal + combatEvent.Amount;
                break;
            case CombatEventType.HoTTick:
                HealingTotal += combatEvent.Amount;
                HoTHealingTotal += combatEvent.Amount;
                OverhealTotal += combatEvent.Overheal;
                HealingEvents++;
                HealingByAction.TryGetValue(combatEvent.ActionId, out var hot);
                HealingByAction[combatEvent.ActionId] = hot + combatEvent.Amount;
                break;
            case CombatEventType.Shield:
                ShieldTotal += combatEvent.Amount;
                ShieldByAction.TryGetValue(combatEvent.ActionId, out var shield);
                ShieldByAction[combatEvent.ActionId] = shield + combatEvent.Amount;
                break;
        }
    }

    public double ActiveTimeSeconds
    {
        get
        {
            if (FirstEventUtc == DateTime.MinValue || LastEventUtc == DateTime.MinValue)
                return 0.001d;

            return Math.Max(0.001d, (LastEventUtc - FirstEventUtc).TotalSeconds);
        }
    }

    public double Dps => DamageTotal / ActiveTimeSeconds;
    public double Hps => HealingTotal / ActiveTimeSeconds;
    public double Dpm => DamageTotal / ActiveTimeSeconds * 60d;
    public double Hpm => HealingTotal / ActiveTimeSeconds * 60d;

    public double GetDamagePerSecond(double encounterSeconds)
        => DamageTotal / Math.Max(0.001d, encounterSeconds);

    public double GetHealingPerSecond(double encounterSeconds)
        => HealingTotal / Math.Max(0.001d, encounterSeconds);

    public double GetDamagePerMinute(double encounterSeconds)
        => GetDamagePerSecond(encounterSeconds) * 60d;

    public double GetHealingPerMinute(double encounterSeconds)
        => GetHealingPerSecond(encounterSeconds) * 60d;

    public double OverhealPercent => HealingTotal + OverhealTotal <= 0 ? 0d : (double)OverhealTotal / (HealingTotal + OverhealTotal) * 100d;
    public double RaidContribution => 0d;

    public IEnumerable<KeyValuePair<uint, long>> TopDamageActions(int maxRows)
        => DamageByAction.OrderByDescending(x => x.Value).Take(maxRows);

    public IEnumerable<KeyValuePair<uint, long>> TopHealingActions(int maxRows)
        => HealingByAction.OrderByDescending(x => x.Value).Take(maxRows);
}
