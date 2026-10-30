using Loyalty.Core.Modules.Campaigns.Application;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Integration.Domain;
using Loyalty.Core.Modules.Rules;
using Loyalty.Core.Modules.Settings.Domain;
using Shouldly;

namespace Loyalty.Tests.Unit;

public class RewardDecisionTests
{
    private static NormalizedEvent Purchase(decimal amount, Dictionary<string, object?>? facts = null) =>
        new("PurchaseCompleted", 1, "sys|order-1", amount, "IQD",
            facts ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["amount"] = amount });

    private static Campaign Campaign(IEnumerable<CampaignCondition> conds, IEnumerable<CampaignEffect> effects, StackingPolicy stacking = StackingPolicy.Stackable, int priority = 100)
    {
        var c = Loyalty.Core.Modules.Campaigns.Domain.Campaign.Create(
            Guid.NewGuid(), Guid.NewGuid(), "c", null, "PurchaseCompleted", 1, null, null, priority, stacking,
            conds, effects, DateTimeOffset.UtcNow);
        c.Activate(DateTimeOffset.UtcNow);
        return c;
    }

    // Reference case from PROCESSING_FLOW §4: 149.50 at 1.00/point, ×1.5, +50 bonus, Floor => 273
    [Fact]
    public void Reference_case_yields_273()
    {
        var decider = new RewardDecisionService(new OperatorRuleEvaluator());
        var campaign = Campaign(
            conds: [],
            effects:
            [
                new CampaignEffect("base", EffectType.AwardBasePoints, Multiplier: 1.5m),
                new CampaignEffect("bonus", EffectType.AwardPoints, Points: 50),
            ]);

        var result = decider.Decide(1.0m, RoundingPolicy.Floor, Purchase(149.50m), [campaign]);

        result.HasReward.ShouldBeTrue();
        result.Effects.Sum(e => e.Points).ShouldBe(273);
    }

    [Fact]
    public void No_matching_campaign_yields_no_reward()
    {
        var decider = new RewardDecisionService(new OperatorRuleEvaluator());
        var campaign = Campaign(
            conds: [new CampaignCondition("attributes.channel", ConditionOperator.Equals, "mobile")],
            effects: [new CampaignEffect("base", EffectType.AwardBasePoints)]);

        var facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["amount"] = 1000m,
            ["attributes.channel"] = "web",
        };
        var result = decider.Decide(1.0m, RoundingPolicy.Floor, Purchase(1000m, facts), [campaign]);
        result.HasReward.ShouldBeFalse();
    }

    [Fact]
    public void Exclusive_highest_priority_suppresses_others()
    {
        var decider = new RewardDecisionService(new OperatorRuleEvaluator());
        var exclusive = Campaign([], [new CampaignEffect("x", EffectType.AwardPoints, Points: 100)], StackingPolicy.Exclusive, priority: 200);
        var stackable = Campaign([], [new CampaignEffect("y", EffectType.AwardPoints, Points: 10)], StackingPolicy.Stackable, priority: 100);

        var result = decider.Decide(1.0m, RoundingPolicy.Floor, Purchase(1000m), [stackable, exclusive]);

        result.Effects.Count.ShouldBe(1);
        result.Effects[0].Points.ShouldBe(100);
    }
}
