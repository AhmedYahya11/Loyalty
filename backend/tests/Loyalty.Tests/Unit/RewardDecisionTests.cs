using Loyalty.Core.Modules.Campaigns.Application;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Rules;
using Loyalty.Core.Modules.Settings.Domain;
using Shouldly;

namespace Loyalty.Tests.Unit;

public class RewardDecisionTests
{
    private static readonly RewardDecisionService Decider = new(new OperatorRuleEvaluator());

    private static NormalizedEvent Purchase(decimal amount, Dictionary<string, object?>? facts = null) =>
        new("PurchaseCompleted", 1, "sys|order-1", amount, "IQD", null,
            facts ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["amount"] = amount });

    private static Campaign Campaign(IEnumerable<CampaignCondition> conds, IEnumerable<CampaignEffect> effects,
        StackingPolicy stacking = StackingPolicy.Stackable, int priority = 100)
    {
        var c = Loyalty.Core.Modules.Campaigns.Domain.Campaign.Create(
            Guid.NewGuid(), Guid.NewGuid(), "c", null, "PurchaseCompleted", 1, null, null, priority, stacking,
            conds, effects, null, null, null, null, null, DateTimeOffset.UtcNow);
        c.Validate(DateTimeOffset.UtcNow);
        c.Activate(DateTimeOffset.UtcNow);
        return c;
    }

    private static long Total(Campaign[] candidates, decimal amount)
    {
        var eligible = Decider.SelectEligible(Purchase(amount), candidates);
        return Decider.ComputeStackedEffects(1.0m, RoundingPolicy.Floor, Purchase(amount), eligible).Effects.Sum(e => e.Points);
    }

    // Reference case from PROCESSING_FLOW §4: 149.50 at 1.00/point, ×1.5, +50 bonus, Floor => 273
    [Fact]
    public void Reference_case_yields_273()
    {
        var campaign = Campaign([],
        [
            new CampaignEffect("base", EffectType.AwardBasePoints, Multiplier: 1.5m),
            new CampaignEffect("bonus", EffectType.AwardPoints, Points: 50),
        ]);
        Total([campaign], 149.50m).ShouldBe(273);
    }

    [Fact]
    public void Base_counted_once_with_highest_multiplier_across_campaigns()
    {
        // two campaigns both award base points; base must count ONCE at the highest multiplier (×2)
        var a = Campaign([], [new CampaignEffect("base", EffectType.AwardBasePoints, Multiplier: 1m)], priority: 100);
        var b = Campaign([], [new CampaignEffect("base", EffectType.AwardBasePoints, Multiplier: 2m)], priority: 90);
        // base = floor(100/1) = 100; ×2 = 200 (counted once), no double 300
        Total([a, b], 100m).ShouldBe(200);
    }

    [Fact]
    public void No_matching_campaign_yields_no_reward()
    {
        var campaign = Campaign(
            [new CampaignCondition("attributes.channel", ConditionOperator.Equals, "mobile")],
            [new CampaignEffect("base", EffectType.AwardBasePoints)]);
        var facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["amount"] = 1000m, ["attributes.channel"] = "web" };
        var eligible = Decider.SelectEligible(Purchase(1000m, facts), [campaign]);
        eligible.Count.ShouldBe(0);
    }

    [Fact]
    public void Exclusive_highest_priority_suppresses_others()
    {
        var exclusive = Campaign([], [new CampaignEffect("x", EffectType.AwardPoints, Points: 100)], StackingPolicy.Exclusive, priority: 200);
        var stackable = Campaign([], [new CampaignEffect("y", EffectType.AwardPoints, Points: 10)], StackingPolicy.Stackable, priority: 100);
        Total([stackable, exclusive], 1000m).ShouldBe(100);
    }
}
