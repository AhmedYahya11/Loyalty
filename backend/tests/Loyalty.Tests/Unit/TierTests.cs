using Loyalty.Core.Modules.Tiers.Domain;
using Shouldly;

namespace Loyalty.Tests.Unit;

public class TierTests
{
    private static readonly Guid App = Guid.NewGuid();
    private static Tier T(string name, int rank, TierMetric metric, decimal threshold, decimal mult) =>
        Tier.Create(App, name, null, rank, metric, threshold, mult, null, DateTimeOffset.UtcNow);

    private static readonly List<Tier> Tiers =
    [
        T("Bronze", 0, TierMetric.NetSpend, 0, 1.0m),
        T("Silver", 1, TierMetric.NetSpend, 100_000, 1.1m),
        T("Gold", 2, TierMetric.NetSpend, 500_000, 1.25m),
        T("Platinum", 3, TierMetric.NetSpend, 1_000_000, 1.5m),
    ];

    [Theory]
    [InlineData(0, "Bronze")]
    [InlineData(50_000, "Bronze")]
    [InlineData(100_000, "Silver")]
    [InlineData(250_000, "Silver")]
    [InlineData(500_000, "Gold")]
    [InlineData(2_000_000, "Platinum")]
    public void Member_lands_in_highest_met_tier(int netSpend, string expected)
    {
        TierEvaluator.Evaluate(netSpend, 0, 0, Tiers)!.Name.ShouldBe(expected);
    }

    [Fact]
    public void No_base_tier_means_no_tier()
    {
        var onlySilverUp = Tiers.Where(t => t.Rank > 0).ToList();
        TierEvaluator.Evaluate(50_000, 0, 0, onlySilverUp).ShouldBeNull();
    }

    [Fact]
    public void Inactive_tiers_are_ignored()
    {
        var gold = T("Gold", 2, TierMetric.NetSpend, 500_000, 1.25m);
        gold.Update("Gold", null, 500_000, 1.25m, null, isActive: false, DateTimeOffset.UtcNow);
        var set = new List<Tier> { T("Bronze", 0, TierMetric.NetSpend, 0, 1m), gold };
        TierEvaluator.Evaluate(900_000, 0, 0, set)!.Name.ShouldBe("Bronze");
    }
}
