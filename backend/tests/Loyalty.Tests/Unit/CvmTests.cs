using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Rules;
using Shouldly;

namespace Loyalty.Tests.Unit;

public class CvmTests
{
    private static readonly DateTimeOffset Eval = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid App = Guid.NewGuid();

    private static ApplicationCvmSettings Settings() => ApplicationCvmSettings.Create(App,
        new CvmFields(CvmStatus.Active, 30, 30, 90, 180, 1_000_000m, 5, 365), Eval);

    private static MemberValueProfile Profile(DateTimeOffset firstActivity, DateTimeOffset? lastActivity = null)
    {
        var p = MemberValueProfile.Create(App, Guid.NewGuid(), "IQD", firstActivity);
        if (lastActivity is { } la) p.ApplyLogin(la); // sets LastActivity = max(first, la)
        return p;
    }

    [Fact]
    public void New_within_new_period()
    {
        var p = Profile(Eval.AddDays(-10));
        LifecycleEvaluator.Evaluate(p, Settings(), Eval).ShouldBe(LifecycleStage.New);
    }

    [Theory]
    [InlineData(5, LifecycleStage.Active)]
    [InlineData(40, LifecycleStage.AtRisk)]
    [InlineData(100, LifecycleStage.Dormant)]
    [InlineData(200, LifecycleStage.Churned)]
    public void Inactivity_thresholds(int inactivityDays, LifecycleStage expected)
    {
        var p = Profile(Eval.AddDays(-365), Eval.AddDays(-inactivityDays));
        LifecycleEvaluator.Evaluate(p, Settings(), Eval).ShouldBe(expected);
    }

    [Fact]
    public void Purchase_projection_updates_count_and_spend()
    {
        var p = Profile(Eval.AddDays(-10));
        p.ApplyPurchase(Eval, 50_000m);
        p.ApplyPurchase(Eval, 25_000m);
        p.PurchaseCount.ShouldBe(2);
        p.NetSpendAmount.ShouldBe(75_000m);
        p.LastPurchaseAt.ShouldBe(Eval);
    }

    [Fact]
    public void Refund_reduces_net_spend_but_not_below_zero()
    {
        var p = Profile(Eval.AddDays(-10));
        p.ApplyPurchase(Eval, 50_000m);
        p.ApplyRefund(30_000m, Eval);
        p.NetSpendAmount.ShouldBe(20_000m);
        p.ApplyRefund(999_000m, Eval);
        p.NetSpendAmount.ShouldBe(0m);
    }

    [Fact]
    public void Delayed_event_moves_first_activity_earlier_not_last_backward()
    {
        var p = Profile(Eval.AddDays(-10));            // first = last = Eval-10
        p.ApplyLogin(Eval);                             // last -> Eval
        p.ApplyLogin(Eval.AddDays(-50));                // delayed: first -> Eval-50, last stays Eval
        p.FirstActivityAt.ShouldBe(Eval.AddDays(-50));
        p.LastActivityAt.ShouldBe(Eval);
    }

    [Fact]
    public void Segment_conditions_evaluate_over_profile_facts()
    {
        var evaluator = new OperatorRuleEvaluator();
        var p = Profile(Eval.AddDays(-10));
        p.ApplyPurchase(Eval, 2_000_000m);
        var facts = ProfileFacts.Of(p);

        var highValue = new List<CampaignCondition> { new("netSpendAmount", ConditionOperator.GreaterThanOrEqual, "1000000") };
        evaluator.Evaluate(highValue, facts).Matched.ShouldBeTrue();

        var newOnly = new List<CampaignCondition> { new("lifecycleStage", ConditionOperator.Equals, "New") };
        evaluator.Evaluate(newOnly, facts).Matched.ShouldBeTrue(); // profile starts as New
    }
}
