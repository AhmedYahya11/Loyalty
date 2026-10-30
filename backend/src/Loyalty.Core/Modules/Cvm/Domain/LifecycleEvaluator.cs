namespace Loyalty.Core.Modules.Cvm.Domain;

/// <summary>
/// Deterministic lifecycle evaluation from a single captured UTC timestamp (MVP spec §11.3).
/// A member within NewPeriodDays of first activity is New; otherwise inactivity thresholds are
/// checked highest→lowest (Churned, Dormant, AtRisk); below at-risk is Active.
/// </summary>
public static class LifecycleEvaluator
{
    public static LifecycleStage Evaluate(MemberValueProfile profile, ApplicationCvmSettings cvm, DateTimeOffset evaluatedAt)
    {
        var first = profile.FirstActivityAt ?? evaluatedAt;
        var last = profile.LastActivityAt ?? first;

        var ageDays = (evaluatedAt - first).TotalDays;
        if (ageDays <= cvm.NewPeriodDays) return LifecycleStage.New;

        var inactivityDays = (evaluatedAt - last).TotalDays;
        if (inactivityDays >= cvm.ChurnedAfterDays) return LifecycleStage.Churned;
        if (inactivityDays >= cvm.DormantAfterDays) return LifecycleStage.Dormant;
        if (inactivityDays >= cvm.AtRiskAfterDays) return LifecycleStage.AtRisk;
        return LifecycleStage.Active;
    }
}
