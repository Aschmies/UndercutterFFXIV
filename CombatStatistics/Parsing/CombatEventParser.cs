using CombatStatistics.Models;

namespace CombatStatistics.Parsing;

public sealed class CombatEventParser
{
    public CombatEvent CreateDamageEvent(
        ActorIdentity source,
        ActorIdentity target,
        uint actionId,
        int amount,
        bool isCritical = false,
        bool isDirectHit = false,
        bool isDoT = false,
        bool isPet = false)
        => new()
        {
            Type = isDoT ? CombatEventType.DoTTick : CombatEventType.Damage,
            Source = source,
            Target = target,
            ActionId = actionId,
            Amount = amount,
            IsCritical = isCritical,
            IsDirectHit = isDirectHit,
            IsPet = isPet,
            IsDamageOverTime = isDoT,
        };

    public CombatEvent CreateHealEvent(
        ActorIdentity source,
        ActorIdentity target,
        uint actionId,
        int amount,
        int overheal = 0,
        bool isCritical = false,
        bool isDirectHit = false,
        bool isHoT = false,
        bool isPet = false)
        => new()
        {
            Type = isHoT ? CombatEventType.HoTTick : CombatEventType.Heal,
            Source = source,
            Target = target,
            ActionId = actionId,
            Amount = amount,
            Overheal = overheal,
            IsCritical = isCritical,
            IsDirectHit = isDirectHit,
            IsPet = isPet,
            IsHealingOverTime = isHoT,
        };

    public CombatEvent CreateShieldEvent(
        ActorIdentity source,
        ActorIdentity target,
        uint actionId,
        int amount,
        bool isPet = false)
        => new()
        {
            Type = CombatEventType.Shield,
            Source = source,
            Target = target,
            ActionId = actionId,
            Amount = amount,
            IsPet = isPet,
        };
}
