using System.Dynamic;
using System.Globalization;
using System.Text;
using Loyalty.Core.Modules.Campaigns.Domain;
using RulesEngine.Models;

namespace Loyalty.Core.Modules.Rules;

/// <summary>
/// Microsoft RulesEngine-backed implementation of <see cref="ILoyaltyRuleEvaluator"/> (MVP spec
/// §12). Conditions from the controlled operator registry are compiled to RulesEngine lambda
/// expressions over a sanitized fact object. Selectable via the LOYALTY_RULES_ENGINE env var;
/// the deterministic <see cref="OperatorRuleEvaluator"/> remains the default.
/// </summary>
public sealed class RulesEngineRuleEvaluator : ILoyaltyRuleEvaluator
{
    private static readonly ReSettings Settings = new() { CustomTypes = [typeof(Convert)] };

    public RuleEvaluationResult Evaluate(
        IReadOnlyList<CampaignCondition> conditions, IReadOnlyDictionary<string, object?> facts)
    {
        if (conditions.Count == 0) return new RuleEvaluationResult(true, []);

        var expando = new ExpandoObject();
        var bag = (IDictionary<string, object?>)expando;
        foreach (var (k, v) in facts) bag[Sanitize(k)] = v is null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);

        var rules = conditions.Select((c, i) => new Rule
        {
            RuleName = $"r{i}",
            RuleExpressionType = RuleExpressionType.LambdaExpression,
            Expression = BuildExpression(c),
        }).ToList();

        var workflow = new Workflow { WorkflowName = "conditions", Rules = rules };
        var engine = new RulesEngine.RulesEngine([workflow], Settings);

        var results = engine.ExecuteAllRulesAsync("conditions", new RuleParameter("input1", expando))
            .GetAwaiter().GetResult();

        var matched = results.All(r => r.IsSuccess);
        var names = matched ? conditions.Select(c => $"{c.Field} {c.Operator} {c.Value}").ToList() : [];
        return new RuleEvaluationResult(matched, names);
    }

    private static string BuildExpression(CampaignCondition c)
    {
        var sf = Sanitize(c.Field);
        var s = $"Convert.ToString(input1.{sf})"; // all facts stored as strings
        return c.Operator switch
        {
            ConditionOperator.Equals => $"{s}.ToLower() == \"{Lit(c.Value)}\"",
            ConditionOperator.NotEquals => $"{s}.ToLower() != \"{Lit(c.Value)}\"",
            ConditionOperator.GreaterThan => $"Convert.ToDecimal({s}) > {Num(c.Value)}",
            ConditionOperator.GreaterThanOrEqual => $"Convert.ToDecimal({s}) >= {Num(c.Value)}",
            ConditionOperator.LessThan => $"Convert.ToDecimal({s}) < {Num(c.Value)}",
            ConditionOperator.LessThanOrEqual => $"Convert.ToDecimal({s}) <= {Num(c.Value)}",
            ConditionOperator.In => BuildIn(s, c.Value),
            _ => "true",
        };
    }

    private static string BuildIn(string s, string csv)
    {
        var items = csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (items.Length == 0) return "false";
        var sb = new StringBuilder();
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0) sb.Append(" || ");
            sb.Append($"{s}.ToLower() == \"{Lit(items[i])}\"");
        }
        return sb.ToString();
    }

    private static string Lit(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"").ToLowerInvariant();
    private static string Num(string v) => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
        ? d.ToString(CultureInfo.InvariantCulture) : "0";

    private static string Sanitize(string field)
    {
        var sb = new StringBuilder(field.Length);
        foreach (var ch in field) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }
}
