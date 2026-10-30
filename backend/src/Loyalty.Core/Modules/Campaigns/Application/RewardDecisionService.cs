using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Rules;
using Loyalty.Core.Modules.Settings.Domain;

namespace Loyalty.Core.Modules.Campaigns.Application;

public sealed record RewardComputedEffect(Guid CampaignId, int CampaignVersion, string EffectId, long Points);
public sealed record RewardDecisionResult(IReadOnlyList<RewardComputedEffect> Effects)
{
    public bool HasReward => Effects.Count > 0;
}

/// <summary>
/// Pure reward computation over loaded state (MVP spec §12, PROCESSING_FLOW diagram 4). No
/// writes happen here — it returns typed effects that the handler persists. Selects matching
/// campaigns, applies exclusivity by priority, and computes points per effect.
///
/// NOTE (Phase 1): per-campaign-effect computation (base × multiplier, fixed bonuses). The
/// full cross-campaign stacking algorithm (one base + stackable bonuses × highest eligible
/// multiplier), member limits, and budgets are completed with the campaign engine in P3.
/// </summary>
public sealed class RewardDecisionService(ILoyaltyRuleEvaluator evaluator)
{
    public RewardDecisionResult Decide(
        decimal spendAmountPerPoint,
        RoundingPolicy rounding,
        NormalizedEvent normalized,
        IReadOnlyList<Campaign> candidates)
    {
        // keep only campaigns whose conditions match the event facts
        var matched = candidates
            .Where(c => evaluator.Evaluate(c.Conditions, normalized.Facts).Matched)
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Id)
            .ToList();

        if (matched.Count == 0) return new RewardDecisionResult([]);

        // exclusivity: if the highest-priority match is exclusive, suppress the rest
        var eligible = matched[0].StackingPolicy == StackingPolicy.Exclusive
            ? [matched[0]]
            : matched;

        var effects = new List<RewardComputedEffect>();
        var basePoints = normalized.Amount is { } amt
            ? PointsMath.CalculateBasePoints(amt, spendAmountPerPoint, rounding)
            : 0;

        foreach (var c in eligible)
        {
            foreach (var e in c.Effects)
            {
                long points = e.Type switch
                {
                    EffectType.AwardBasePoints => (long)Math.Floor(basePoints * (double)(e.Multiplier ?? 1m)),
                    EffectType.AwardPoints => e.Points ?? 0,
                    _ => 0, // ReversePoints / AdjustPoints / NoReward not applied in the purchase-earn slice
                };
                if (points > 0)
                    effects.Add(new RewardComputedEffect(c.Id, c.Version, e.EffectId, points));
            }
        }

        return new RewardDecisionResult(effects);
    }
}
