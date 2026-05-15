using CombatStatistics.Models;
using Dalamud.Game.Chat;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CombatStatistics.Parsing;

public sealed class CombatEventParser
{
    private static readonly Regex AmountRegex = new(@"[0-9][0-9,]*", RegexOptions.Compiled);

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

    public ActorType DetermineSourceType(ILogMessage message, CombatEventType eventType)
    {
        if (message.SourceEntity?.IsPlayer == true)
            return ActorType.Player;

        return eventType switch
        {
            CombatEventType.Heal or CombatEventType.HoTTick or CombatEventType.Shield => ActorType.Player,
            _ => ActorType.Enemy,
        };
    }

    public ActorType DetermineTargetType(ILogMessage message, CombatEventType eventType)
    {
        if (message.TargetEntity?.IsPlayer == true)
            return ActorType.Player;

        return eventType switch
        {
            CombatEventType.Heal or CombatEventType.HoTTick or CombatEventType.Shield => ActorType.Player,
            _ => ActorType.Enemy,
        };
    }

    private static bool TryParseDamage(ILogMessage message, string formatted, DateTime timestampUtc, out CombatEvent combatEvent)
    {
        if (!LooksLikeDamage(formatted))
        {
            combatEvent = default!;
            return false;
        }

        var amount = ParseLastAmount(formatted);
        if (amount <= 0)
        {
            combatEvent = default!;
            return false;
        }

        combatEvent = new CombatEvent
        {
            TimestampUtc = timestampUtc,
            Type = CombatEventType.Damage,
            Source = new ActorIdentity(),
            Target = new ActorIdentity(),
            Amount = amount,
            ActionId = message.LogMessageId,
            IsCritical = formatted.Contains("critical", StringComparison.OrdinalIgnoreCase),
            IsDirectHit = formatted.Contains("direct hit", StringComparison.OrdinalIgnoreCase),
        };
        return true;
    }

    private static bool TryParseHeal(ILogMessage message, string formatted, DateTime timestampUtc, out CombatEvent combatEvent)
    {
        if (!LooksLikeHeal(formatted))
        {
            combatEvent = default!;
            return false;
        }

        var amount = ParseLastAmount(formatted);
        if (amount <= 0)
        {
            combatEvent = default!;
            return false;
        }

        combatEvent = new CombatEvent
        {
            TimestampUtc = timestampUtc,
            Type = CombatEventType.Heal,
            Source = new ActorIdentity(),
            Target = new ActorIdentity(),
            Amount = amount,
            Overheal = 0,
            ActionId = message.LogMessageId,
            IsCritical = formatted.Contains("critical", StringComparison.OrdinalIgnoreCase),
        };
        return true;
    }

    private static bool TryParseShield(ILogMessage message, string formatted, DateTime timestampUtc, out CombatEvent combatEvent)
    {
        if (!LooksLikeShield(formatted))
        {
            combatEvent = default!;
            return false;
        }

        var amount = ParseLastAmount(formatted);
        if (amount <= 0)
        {
            combatEvent = default!;
            return false;
        }

        combatEvent = new CombatEvent
        {
            TimestampUtc = timestampUtc,
            Type = CombatEventType.Shield,
            Source = new ActorIdentity(),
            Target = new ActorIdentity(),
            Amount = amount,
            ActionId = message.LogMessageId,
        };
        return true;
    }

    private static bool LooksLikeDamage(string text)
        => text.Contains("damage", StringComparison.OrdinalIgnoreCase)
            && !LooksLikeHeal(text)
            && !LooksLikeShield(text);

    private static bool LooksLikeHeal(string text)
        => text.Contains(" HP", StringComparison.OrdinalIgnoreCase)
            || text.Contains("heals", StringComparison.OrdinalIgnoreCase)
            || text.Contains("restores", StringComparison.OrdinalIgnoreCase)
            || text.Contains("recovers", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeShield(string text)
        => text.Contains("barrier", StringComparison.OrdinalIgnoreCase)
            || text.Contains("absorbs", StringComparison.OrdinalIgnoreCase);

    private static int ParseLastAmount(string value)
    {
        var match = AmountRegex.Matches(value).Cast<Match>().LastOrDefault();
        if (match == null)
            return 0;

        return int.TryParse(match.Value.Replace(",", string.Empty), out var amount) ? amount : 0;
    }

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
