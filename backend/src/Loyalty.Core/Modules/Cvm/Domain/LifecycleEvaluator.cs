namespace Loyalty.Core.Modules.Cvm.Domain;

/// <summary>The lifecycle day windows (from live settings or a frozen snapshot).</summary>
public sealed record CvmThresholds(int NewPeriodDays, int AtRiskAfterDays, int DormantAfterDays, int ChurnedAfterDays);

/// <summary>
/// Deterministic lifecycle evaluation from a single captured UTC timestamp (MVP spec §11.3).
/// A member within NewPeriodDays of first activity is New; otherwise inactivity thresholds are
/// checked highest→lowest (Churned, Dormant, AtRisk); below at-risk is Active.
/// </summary>
public static class LifecycleEvaluator
{
    public static LifecycleStage Evaluate(MemberValueProfile profile, ApplicationCvmSettings cvm, DateTimeOffset evaluatedAt) =>
        Evaluate(profile, new CvmThresholds(cvm.NewPeriodDays, cvm.AtRiskAfterDays, cvm.DormantAfterDays, cvm.ChurnedAfterDays), evaluatedAt);

    public static LifecycleStage Evaluate(MemberValueProfile profile, CvmThresholds t, DateTimeOffset evaluatedAt)
    {
        var first = profile.FirstActivityAt ?? evaluatedAt;
        var last = profile.LastActivityAt ?? first;

        var ageDays = (evaluatedAt - first).TotalDays;
        if (ageDays <= t.NewPeriodDays) return LifecycleStage.New;

        var inactivityDays = (evaluatedAt - last).TotalDays;
        if (inactivityDays >= t.ChurnedAfterDays) return LifecycleStage.Churned;
        if (inactivityDays >= t.DormantAfterDays) return LifecycleStage.Dormant;
        if (inactivityDays >= t.AtRiskAfterDays) return LifecycleStage.AtRisk;
        return LifecycleStage.Active;
    }
}
