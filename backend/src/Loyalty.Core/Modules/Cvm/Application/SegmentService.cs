using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Campaigns.Domain;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Rules;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Cvm.Application;

public sealed record CreateSegmentInput(string Name, string? NameAr, IReadOnlyList<CampaignCondition> Conditions);
public sealed record SegmentView(Guid Id, string Name, string? NameAr, string Status, int CurrentVersion, IReadOnlyList<CampaignCondition> Conditions);

/// <summary>
/// Owns rule-based segment definition, versioning, and membership evaluation (MVP spec §11.4).
/// Conditions use a controlled field + operator registry (no arbitrary scripts).
///
/// NOTE (Phase 2): activation evaluates existing profiles synchronously in bounded order and
/// returns the Active segment. The spec's 202 + background activation for very large member
/// bases is a later enhancement (docs/DEVIATIONS.md).
/// </summary>
public sealed class SegmentService(LoyaltyDbContext db, ApplicationContext scope, ILoyaltyRuleEvaluator evaluator, IClock clock)
{
    public async Task<SegmentView> CreateAsync(CreateSegmentInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        ValidateConditions(input.Conditions);

        var now = clock.UtcNow;
        var seg = SegmentDefinition.Create(Guid.NewGuid(), scope.ApplicationId, input.Name, input.NameAr, now);
        db.Segments.Add(seg);
        db.SegmentVersions.Add(SegmentVersion.Create(scope.ApplicationId, seg.Id, 1, input.Conditions, scope.AccountId, now));
        await db.SaveChangesAsync(ct);
        return ToView(seg, input.Conditions);
    }

    public async Task<SegmentView?> ValidateAsync(Guid id, CancellationToken ct)
    {
        var seg = await db.Segments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (seg is null) return null;
        var conds = await CurrentConditions(seg, ct);
        ValidateConditions(conds);
        seg.SetStatus(SegmentStatus.Validated, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(seg, conds);
    }

    public async Task<SegmentView?> ActivateAsync(Guid id, CancellationToken ct)
    {
        var seg = await db.Segments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (seg is null) return null;

        var version = await db.SegmentVersions.FirstAsync(v => v.SegmentId == seg.Id && v.Version == seg.CurrentVersion, ct);
        ValidateConditions(version.Conditions);

        var evaluatedAt = clock.UtcNow;
        seg.SetStatus(SegmentStatus.Activating, evaluatedAt);
        await db.SaveChangesAsync(ct);

        // evaluate existing profiles → memberships (one captured timestamp)
        var profiles = await db.ValueProfiles.Where(p => p.ApplicationId == scope.ApplicationId).ToListAsync(ct);
        foreach (var p in profiles)
        {
            var matches = evaluator.Evaluate(version.Conditions, ProfileFacts.Of(p)).Matched;
            var active = await db.SegmentMemberships.FirstOrDefaultAsync(
                m => m.MemberId == p.MemberId && m.SegmentId == seg.Id && m.IsActive, ct);
            if (matches && active is null)
                db.SegmentMemberships.Add(MemberSegmentMembership.Enter(scope.ApplicationId, p.MemberId, seg.Id, version.Version, p.Version, evaluatedAt));
            else if (!matches && active is not null)
                active.Exit(p.Version, evaluatedAt);
        }

        seg.SetStatus(SegmentStatus.Active, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(seg, version.Conditions);
    }

    public async Task<SegmentView?> PauseAsync(Guid id, CancellationToken ct)
    {
        var seg = await db.Segments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (seg is null) return null;
        seg.SetStatus(SegmentStatus.Paused, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToView(seg, await CurrentConditions(seg, ct));
    }

    public async Task<IReadOnlyList<SegmentView>> ListAsync(CancellationToken ct)
    {
        var segs = await db.Segments.OrderBy(s => s.Name).ToListAsync(ct);
        var result = new List<SegmentView>();
        foreach (var s in segs) result.Add(ToView(s, await CurrentConditions(s, ct)));
        return result;
    }

    public async Task<SegmentView?> GetAsync(Guid id, CancellationToken ct)
    {
        var seg = await db.Segments.FirstOrDefaultAsync(s => s.Id == id, ct);
        return seg is null ? null : ToView(seg, await CurrentConditions(seg, ct));
    }

    private async Task<List<CampaignCondition>> CurrentConditions(SegmentDefinition seg, CancellationToken ct) =>
        (await db.SegmentVersions.FirstOrDefaultAsync(v => v.SegmentId == seg.Id && v.Version == seg.CurrentVersion, ct))?.Conditions ?? [];

    private static void ValidateConditions(IReadOnlyList<CampaignCondition> conditions)
    {
        foreach (var c in conditions)
            if (!ProfileFacts.AllowedFields.Contains(c.Field))
                throw new LoyaltyException(LoyaltyError.ValidationFailed(
                    $"Unknown segment field '{c.Field}'.", $"حقل غير معروف في الشريحة: '{c.Field}'."));
    }

    private static SegmentView ToView(SegmentDefinition s, IReadOnlyList<CampaignCondition> conds) =>
        new(s.Id, s.Name, s.NameAr, s.Status.ToString(), s.CurrentVersion, conds);
}
