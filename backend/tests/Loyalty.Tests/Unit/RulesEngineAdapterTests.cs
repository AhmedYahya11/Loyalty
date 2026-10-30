using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Rules;
using Shouldly;

namespace Loyalty.Tests.Unit;

/// <summary>The RulesEngine adapter must agree with the deterministic operator evaluator.</summary>
public class RulesEngineAdapterTests
{
    private static readonly RulesEngineRuleEvaluator Rules = new();
    private static readonly OperatorRuleEvaluator Operator = new();

    private static Dictionary<string, object?> Facts(params (string, object?)[] kv)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Theory]
    [InlineData("lifecycleStage", "Equals", "New", "New", true)]
    [InlineData("lifecycleStage", "Equals", "New", "Active", false)]
    [InlineData("netSpendAmount", "GreaterThanOrEqual", "1000000", "2000000", true)]
    [InlineData("netSpendAmount", "GreaterThanOrEqual", "1000000", "500000", false)]
    [InlineData("attributes.channel", "Equals", "mobile", "MOBILE", true)]
    [InlineData("attributes.channel", "Equals", "mobile", "web", false)]
    public void Adapter_agrees_with_operator_evaluator(string field, string op, string value, string actual, bool expected)
    {
        var cond = new List<CampaignCondition> { new(field, Enum.Parse<ConditionOperator>(op), value) };
        var facts = Facts((field, actual));

        Operator.Evaluate(cond, facts).Matched.ShouldBe(expected);
        Rules.Evaluate(cond, facts).Matched.ShouldBe(expected);
    }

    [Fact]
    public void In_operator_matches()
    {
        var cond = new List<CampaignCondition> { new("lifecycleStage", ConditionOperator.In, "New,Active") };
        Rules.Evaluate(cond, Facts(("lifecycleStage", "Active"))).Matched.ShouldBeTrue();
        Rules.Evaluate(cond, Facts(("lifecycleStage", "Churned"))).Matched.ShouldBeFalse();
    }
}
