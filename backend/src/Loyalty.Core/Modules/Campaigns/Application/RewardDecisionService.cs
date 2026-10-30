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
/// writes happen here. Eligibility (audience + conditions + exclusivity) is separated from the
/// stacked effect computation so the handler can apply member limits and budget reservation to
/// the eligible set in between.
///
/// Stacking (§12/§14): one base calculation counted once × the highest eligible multiplier,
/// plus the sum of stackable fixed bonuses. The multiplier applies to the base only.
/// </summary>
public sealed class RewardDecisionService(ILoyaltyRuleEvaluator evaluator)
{
    /// <summary>Audience + condition match, ordered by priority; exclusivity suppresses the rest.</summary>
    public IReadOnlyList<Campaign> SelectEligible(
        NormalizedEvent normalized,
        IReadOnlyList<Campaign> candidates,
        string? lifecycleStage = null,
        IReadOnlySet<Guid>? activeSegmentIds = null)
    {
        var segments = activeSegmentIds ?? new HashSet<Guid>();
        var matched = candidates
            .Where(c => c.MatchesAudience(lifecycleStage, segments))
            .Where(c => evaluator.Evaluate(c.Conditions, normalized.Facts).Matched)
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Id)
            .ToList();

        if (matched.Count == 0) return matched;
        return matched[0].StackingPolicy == StackingPolicy.Exclusive ? [matched[0]] : matched;
    }

    /// <summary>Compute stacked effects over an already-eligible, limit/budget-surviving set.</summary>
    public RewardDecisionResult ComputeStackedEffects(
        decimal spendAmountPerPoint, RoundingPolicy rounding, NormalizedEvent normalized, IReadOnlyList<Campaign> eligible)
    {
        if (eligible.Count == 0) return new RewardDecisionResult([]);

        var effects = new List<RewardComputedEffect>();
        var basePoints = normalized.Amount is { } amt
            ? PointsMath.CalculateBasePoints(amt, spendAmountPerPoint, rounding)
            : 0;

        // one base calculation × the highest eligible multiplier, attributed to the highest-priority base campaign
        var baseCampaigns = eligible
            .Where(c => c.Effects.Any(e => e.Type == EffectType.AwardBasePoints))
            .ToList();
        if (basePoints > 0 && baseCampaigns.Count > 0)
        {
            var highestMultiplier = baseCampaigns
                .SelectMany(c => c.Effects)
                .Where(e => e.Type == EffectType.AwardBasePoints)
                .Max(e => e.Multiplier ?? 1m);
            var multipliedBase = (long)Math.Floor(basePoints * highestMultiplier);
            var baseCampaign = baseCampaigns[0];
            var baseEffectId = baseCampaign.Effects.First(e => e.Type == EffectType.AwardBasePoints).EffectId;
            if (multipliedBase > 0)
                effects.Add(new RewardComputedEffect(baseCampaign.Id, baseCampaign.Version, baseEffectId, multipliedBase));
        }

        // stackable fixed bonuses are added afterwards and never multiplied
        foreach (var c in eligible)
            foreach (var e in c.Effects.Where(e => e.Type == EffectType.AwardPoints))
                if ((e.Points ?? 0) > 0)
                    effects.Add(new RewardComputedEffect(c.Id, c.Version, e.EffectId, e.Points!.Value));

        return new RewardDecisionResult(effects);
    }
}
