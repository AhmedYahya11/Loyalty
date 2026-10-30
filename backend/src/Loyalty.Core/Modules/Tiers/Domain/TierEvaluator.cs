namespace Loyalty.Core.Modules.Tiers.Domain;

/// <summary>
/// Deterministic tier evaluation: the member's tier is the highest-rank active tier whose
/// threshold the member's metrics meet. Returns null when no tier qualifies (no base tier).
/// </summary>
public static class TierEvaluator
{
    public static Tier? Evaluate(decimal netSpend, long pointsEarned, long purchaseCount, IEnumerable<Tier> activeTiers) =>
        activeTiers
            .Where(t => t.IsActive && t.Meets(netSpend, pointsEarned, purchaseCount))
            .OrderByDescending(t => t.Rank)
            .FirstOrDefault();
}
