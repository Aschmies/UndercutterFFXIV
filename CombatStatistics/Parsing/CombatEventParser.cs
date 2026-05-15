using CombatStatistics.Models;
using Dalamud.Game.Chat;
using System;
using System.Text.RegularExpressions;

namespace CombatStatistics.Parsing;

public sealed class CombatEventParser
{
    private static readonly Regex DamageRegexes = new(
        @"(?:(?<source>.+?) )?(?:hits|deals damage to|deals) (?<target>.+?) (?:for )?(?<amount>[0-9,]+) (?:damage|points of damage)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HealRegexes = new(
        @"(?:(?<source>.+?) )?(?:heals|restores) (?<target>.+?) (?:for )?(?<amount>[0-9,]+) (?:HP|health)(?: \((?<overheal>[0-9,]+) overheal\))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ShieldRegexes = new(
        @"(?<target>.+?) (?:gains|receives) (?<amount>[0-9,]+) (?:point|points) of barrier",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    public bool TryParse(ILogMessage message, out CombatEvent combatEvent)
    {
        var formatted = message.FormatLogMessageForDebugging().ExtractText();
        var timestampUtc = DateTime.UtcNow;

        if (TryParseDamage(message, formatted, timestampUtc, out combatEvent))
            return true;

        if (TryParseHeal(message, formatted, timestampUtc, out combatEvent))
            return true;

        if (TryParseShield(message, formatted, timestampUtc, out combatEvent))
            return true;

        combatEvent = default!;
        return false;
    }

    private static bool TryParseDamage(ILogMessage message, string formatted, DateTime timestampUtc, out CombatEvent combatEvent)
    {
        var match = DamageRegexes.Match(formatted);
        if (!match.Success)
        {
            combatEvent = default!;
            return false;
        }

        var source = BuildActor(message.SourceEntity, ActorType.Player);
        var target = BuildActor(message.TargetEntity, ActorType.Enemy);
        var amount = ParseAmount(match.Groups["amount"].Value);

        combatEvent = new CombatEvent
        {
            TimestampUtc = timestampUtc,
            Type = CombatEventType.Damage,
            Source = source,
            Target = target,
            Amount = amount,
            ActionId = message.LogMessageId,
            IsCritical = formatted.Contains("critical", StringComparison.OrdinalIgnoreCase),
            IsDirectHit = formatted.Contains("direct hit", StringComparison.OrdinalIgnoreCase),
        };
        return true;
    }

    private static bool TryParseHeal(ILogMessage message, string formatted, DateTime timestampUtc, out CombatEvent combatEvent)
    {
        var match = HealRegexes.Match(formatted);
        if (!match.Success)
        {
            combatEvent = default!;
            return false;
        }

        var source = BuildActor(message.SourceEntity, ActorType.Player);
        var target = BuildActor(message.TargetEntity, ActorType.Player);
        var amount = ParseAmount(match.Groups["amount"].Value);
        var overheal = match.Groups["overheal"].Success ? ParseAmount(match.Groups["overheal"].Value) : 0;

        combatEvent = new CombatEvent
        {
            TimestampUtc = timestampUtc,
            Type = CombatEventType.Heal,
            Source = source,
            Target = target,
            Amount = amount,
            Overheal = overheal,
            ActionId = message.LogMessageId,
            IsCritical = formatted.Contains("critical", StringComparison.OrdinalIgnoreCase),
        };
        return true;
    }

    private static bool TryParseShield(ILogMessage message, string formatted, DateTime timestampUtc, out CombatEvent combatEvent)
    {
        var match = ShieldRegexes.Match(formatted);
        if (!match.Success)
        {
            combatEvent = default!;
            return false;
        }

        var source = BuildActor(message.SourceEntity, ActorType.Player);
        var target = BuildActor(message.TargetEntity, ActorType.Player);
        var amount = ParseAmount(match.Groups["amount"].Value);

        combatEvent = new CombatEvent
        {
            TimestampUtc = timestampUtc,
            Type = CombatEventType.Shield,
            Source = source,
            Target = target,
            Amount = amount,
            ActionId = message.LogMessageId,
        };
        return true;
    }

    private static ActorIdentity BuildActor(ILogMessageEntity? entity, ActorType defaultType)
    {
        if (entity == null)
            return new ActorIdentity { Name = string.Empty, Type = ActorType.Unknown };

        var type = entity.IsPlayer ? ActorType.Player : defaultType;
        return ActorIdentity.FromLogEntity(entity, type);
    }

    private static int ParseAmount(string value)
        => int.TryParse(value.Replace(",", string.Empty), out var amount) ? amount : 0;

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
