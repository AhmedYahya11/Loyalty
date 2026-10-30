using Loyalty.Core.Modules.Campaigns.Domain;

namespace Loyalty.Core.Modules.Rules;

/// <summary>
/// Evaluates campaign/segment conditions against a fact set and returns a deterministic
/// decision. It never writes balances, publishes messages, or calls external systems
/// (MVP spec §12). The loyalty domain owns effects and side effects.
/// </summary>
public interface ILoyaltyRuleEvaluator
{
    RuleEvaluationResult Evaluate(
        IReadOnlyList<CampaignCondition> conditions,
        IReadOnlyDictionary<string, object?> facts);
}

public sealed record RuleEvaluationResult(bool Matched, IReadOnlyList<string> MatchedConditions);
