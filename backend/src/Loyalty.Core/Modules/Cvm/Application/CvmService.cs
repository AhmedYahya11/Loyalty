using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Integration.Contracts;
using Loyalty.Core.Modules.Rules;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Cvm.Application;

/// <summary>
/// Applies accepted events to the member value profile, evaluates lifecycle, and maintains
/// rule-based segment memberships (MVP spec §11.2–§11.5). Pure state changes on tracked
/// entities; the worker owns the atomic commit.
/// </summary>
public sealed class CvmService(IRepository<Loyalty.Core.Modules.Cvm.Domain.SegmentDefinition> segmentsRepo, IRepository<Loyalty.Core.Modules.Cvm.Domain.MemberSegmentMembership> segmentMembershipsRepo, IRepository<Loyalty.Core.Modules.Cvm.Domain.SegmentVersion> segmentVersionsRepo, ILoyaltyRuleEvaluator evaluator, IClock clock)
{
    /// <summary>Project one typed event onto the profile using the event's OccurredAt.</summary>
    public void ApplyEvent(MemberValueProfile profile, string eventType, decimal? amount, DateTimeOffset occurredAt)
    {
        switch (eventType)
        {
            case EventTypes.CustomerRegistered: profile.ApplyRegistration(occurredAt); break;
            case EventTypes.ApplicationLogin: profile.ApplyLogin(occurredAt); break;
            case EventTypes.PurchaseCompleted: profile.ApplyPurchase(occurredAt, amount ?? 0m); break;
            case EventTypes.OrderRefunded: profile.ApplyRefund(amount ?? 0m, occurredAt); break;
            // ReferralCompleted: the referral CREDIT (ReferralCount) is applied to the RESOLVED
            // REFERRER by the ReferralService (P10), not to the event's customer (the referee).
            // ManualAdjustment has no activity projection; it posts an Adjust ledger entry.
        }
    }

    /// <summary>Evaluate the lifecycle stage if active CVM settings exist. Returns true on change.</summary>
    public bool EvaluateLifecycle(MemberValueProfile profile, ApplicationCvmSettings? cvm, DateTimeOffset evaluatedAt)
    {
        if (cvm is null || !cvm.IsActive) return false;
        return EvaluateLifecycle(profile, new CvmThresholds(cvm.NewPeriodDays, cvm.AtRiskAfterDays, cvm.DormantAfterDays, cvm.ChurnedAfterDays), evaluatedAt);
    }

    /// <summary>Evaluate the lifecycle stage from frozen thresholds. Returns true on change.</summary>
    public bool EvaluateLifecycle(MemberValueProfile profile, CvmThresholds? thresholds, DateTimeOffset evaluatedAt)
    {
        if (thresholds is null) return false;
        var stage = LifecycleEvaluator.Evaluate(profile, thresholds, evaluatedAt);
        var changed = stage != profile.LifecycleStage;
        profile.SetLifecycle(stage, evaluatedAt);
        return changed;
    }

    /// <summary>Enter/retain/exit segment memberships for a member based on its current profile.</summary>
    public async Task EvaluateSegmentsAsync(MemberValueProfile profile, DateTimeOffset evaluatedAt, CancellationToken ct)
    {
        var segments = await segmentsRepo.Query()
            .Where(s => s.ApplicationId == profile.ApplicationId && s.Status == SegmentStatus.Active)
            .ToListAsync(ct);
        if (segments.Count == 0) return;

        var facts = ProfileFacts.Of(profile);
        var memberships = await segmentMembershipsRepo.Query()
            .Where(m => m.ApplicationId == profile.ApplicationId && m.MemberId == profile.MemberId && m.IsActive)
            .ToListAsync(ct);

        foreach (var seg in segments)
        {
            var version = await segmentVersionsRepo.Query()
                .FirstOrDefaultAsync(v => v.SegmentId == seg.Id && v.Version == seg.CurrentVersion, ct);
            if (version is null) continue;

            var matches = evaluator.Evaluate(version.Conditions, facts).Matched;
            var active = memberships.FirstOrDefault(m => m.SegmentId == seg.Id);

            if (matches && active is null)
                segmentMembershipsRepo.Add(MemberSegmentMembership.Enter(
                    profile.ApplicationId, profile.MemberId, seg.Id, version.Version, profile.Version, evaluatedAt));
            else if (matches)
                active!.ReEvaluate(profile.Version, evaluatedAt);
            else if (active is not null)
                active.Exit(profile.Version, evaluatedAt);
        }
    }

    public DateTimeOffset Now => clock.UtcNow;
}
