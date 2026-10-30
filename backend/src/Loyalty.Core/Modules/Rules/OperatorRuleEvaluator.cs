using System.Globalization;
using Loyalty.Core.Modules.Campaigns.Domain;

namespace Loyalty.Core.Modules.Rules;

/// <summary>
/// Deterministic evaluator over the controlled operator registry (§12). All conditions must
/// match (logical AND). Date comparisons use values supplied in the fact set, never the
/// system clock, so evaluation is reproducible.
///
/// NOTE (deviation): the spec prescribes Microsoft RulesEngine behind this port. For the
/// Phase-1 vertical slice we use this deterministic implementation; a RulesEngine-backed
/// adapter can replace it without changing any caller. Tracked in docs/DEVIATIONS.md.
/// </summary>
public sealed class OperatorRuleEvaluator : ILoyaltyRuleEvaluator
{
    public RuleEvaluationResult Evaluate(
        IReadOnlyList<CampaignCondition> conditions,
        IReadOnlyDictionary<string, object?> facts)
    {
        var matched = new List<string>();
        foreach (var c in conditions)
        {
            facts.TryGetValue(c.Field, out var actual);
            if (!Matches(c, actual))
                return new RuleEvaluationResult(false, []);
            matched.Add($"{c.Field} {c.Operator} {c.Value}");
        }
        return new RuleEvaluationResult(true, matched);
    }

    private static bool Matches(CampaignCondition c, object? actual) => c.Operator switch
    {
        ConditionOperator.Equals => StringEquals(actual, c.Value),
        ConditionOperator.NotEquals => !StringEquals(actual, c.Value),
        ConditionOperator.GreaterThan => Compare(actual, c.Value) > 0,
        ConditionOperator.GreaterThanOrEqual => Compare(actual, c.Value) >= 0,
        ConditionOperator.LessThan => Compare(actual, c.Value) < 0,
        ConditionOperator.LessThanOrEqual => Compare(actual, c.Value) <= 0,
        ConditionOperator.In => InList(actual, c.Value),
        ConditionOperator.DateRange => InDateRange(actual, c.Value),
        _ => false,
    };

    private static bool StringEquals(object? actual, string expected) =>
        string.Equals(ToStr(actual), expected, StringComparison.OrdinalIgnoreCase);

    private static bool InList(object? actual, string csv) =>
        csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
           .Any(v => string.Equals(v, ToStr(actual), StringComparison.OrdinalIgnoreCase));

    private static int Compare(object? actual, string expected)
    {
        if (decimal.TryParse(ToStr(actual), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
            && decimal.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var e))
            return a.CompareTo(e);
        return string.Compare(ToStr(actual), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Value is "startIso|endIso"; actual must fall within the inclusive range.</summary>
    private static bool InDateRange(object? actual, string range)
    {
        var parts = range.Split('|');
        if (parts.Length != 2) return false;
        if (!DateTimeOffset.TryParse(ToStr(actual), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var a))
            return false;
        var start = DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var s) ? s : (DateTimeOffset?)null;
        var end = DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var en) ? en : (DateTimeOffset?)null;
        return (start is null || a >= start) && (end is null || a <= end);
    }

    private static string? ToStr(object? v) => v switch
    {
        null => null,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };
}
