using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Settings.Application;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Members.Application;

public sealed record WalletView(Guid WalletId, Guid MemberId, string PointsUnitCode, long ActiveBalance, long PendingBalance, long ReservedBalance, int Version);

public sealed record MemberView(
    Guid MemberId, Guid ApplicationId, string ExternalCustomerId, Guid DefaultWalletId, string Status, WalletView Wallet,
    string? ReferralCode, DateOnly? BirthDate);

public sealed record LedgerPostingView(int Sequence, string LedgerAccountCode, string AccountType, string? BalanceBucket, long DebitAmount, long CreditAmount);
public sealed record LedgerTransactionView(
    Guid TransactionId, string TransactionType, string PointsUnitCode, long TotalDebit, long TotalCredit,
    Guid SourceEventId, string SourceSystem, string? SourceTransactionId, Guid? CampaignId, string EffectId,
    DateTimeOffset OccurredAt, IReadOnlyList<LedgerPostingView> Postings);

public sealed class MemberService(
    LoyaltyDbContext db, ApplicationContext scope, LoyaltySettingsService settings,
    MemberProvisioningService provisioning)
{
    public async Task<MemberView> CreateAsync(string externalCustomerId, CancellationToken ct, DateOnly? birthDate = null)
    {
        if (string.IsNullOrWhiteSpace(externalCustomerId))
            throw new LoyaltyException(LoyaltyError.ValidationFailed("externalCustomerId is required."));

        var effective = await settings.ResolveEffectiveAsync(scope.ApplicationId, ct)
            ?? throw new LoyaltyException(LoyaltyError.LoyaltyNotConfigured());

        var (member, _) = await provisioning.EnsureAsync(scope.ApplicationId, externalCustomerId, effective.PointsUnitCode, effective.BaseCurrency, ct, birthDate);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(member.Id, ct))!;
    }

    public async Task<MemberView?> SetBirthDateAsync(Guid memberId, DateOnly? birthDate, CancellationToken ct)
    {
        var m = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (m is null) return null;
        m.SetBirthDate(birthDate, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetAsync(m.Id, ct);
    }

    public async Task<MemberView?> GetAsync(Guid memberId, CancellationToken ct)
    {
        var m = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (m is null) return null;
        var w = await db.Wallets.FirstAsync(x => x.Id == m.DefaultWalletId, ct);
        return new MemberView(m.Id, m.ApplicationId, m.ExternalCustomerId, m.DefaultWalletId, m.Status.ToString(), ToView(w), m.ReferralCode, m.BirthDate);
    }

    public async Task<MemberView?> GetByExternalIdAsync(string externalCustomerId, CancellationToken ct)
    {
        var m = await db.Members.FirstOrDefaultAsync(x => x.ExternalCustomerId == externalCustomerId, ct);
        return m is null ? null : await GetAsync(m.Id, ct);
    }

    public async Task<object?> GetValueProfileAsync(Guid memberId, CancellationToken ct)
    {
        var m = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (m is null) return null;
        var p = await db.ValueProfiles.FirstOrDefaultAsync(x => x.MemberId == memberId, ct);
        if (p is null) return null;

        var segs = await (
            from ms in db.SegmentMemberships
            join s in db.Segments on ms.SegmentId equals s.Id
            where ms.MemberId == memberId && ms.IsActive
            select new { segmentId = s.Id, name = s.Name, nameAr = s.NameAr, segmentVersion = ms.SegmentVersion }).ToListAsync(ct);

        object? tier = null;
        if (p.CurrentTierId is { } tierId)
        {
            var t = await db.Tiers.FirstOrDefaultAsync(x => x.Id == tierId, ct);
            if (t is not null) tier = new { tierId = t.Id, name = t.Name, nameAr = t.NameAr, rank = t.Rank, earnMultiplier = t.EarnMultiplier, color = t.Color };
        }

        return new
        {
            memberId = p.MemberId,
            lifecycleStage = p.LifecycleStage.ToString(),
            tier,
            currentStreak = p.CurrentStreak,
            longestStreak = p.LongestStreak,
            rfm = new { recency = p.RfmRecency, frequency = p.RfmFrequency, monetary = p.RfmMonetary, persona = p.RfmPersona, evaluatedAt = p.RfmEvaluatedAt },
            p.FirstActivityAt,
            p.LastActivityAt,
            p.LastPurchaseAt,
            p.PurchaseCount,
            p.NetSpendAmount,
            p.BaseCurrency,
            p.LoginCount,
            p.ReferralCount,
            p.PointsEarned,
            p.PointsRedeemed,
            p.PointsExpired,
            profileVersion = p.Version,
            activeSegments = segs,
        };
    }

    public async Task<IReadOnlyList<object>?> GetAchievementsAsync(Guid memberId, CancellationToken ct)
    {
        var m = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (m is null) return null;
        return await (
            from ma in db.MemberAchievements
            join a in db.Achievements on ma.AchievementId equals a.Id
            where ma.MemberId == memberId
            orderby ma.UnlockedAt descending
            select (object)new { achievementId = a.Id, name = a.Name, nameAr = a.NameAr, icon = a.Icon, a.BonusPoints, ma.UnlockedAt }).ToListAsync(ct);
    }

    public async Task<object?> GetSegmentsAsync(Guid memberId, CancellationToken ct)
    {
        var m = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (m is null) return null;
        var segs = await (
            from ms in db.SegmentMemberships
            join s in db.Segments on ms.SegmentId equals s.Id
            where ms.MemberId == memberId && ms.IsActive
            select new { segmentId = s.Id, name = s.Name, nameAr = s.NameAr, segmentVersion = ms.SegmentVersion, ms.EnteredAt }).ToListAsync(ct);
        return new { items = segs };
    }

    public async Task<WalletView?> GetWalletAsync(Guid memberId, CancellationToken ct)
    {
        var m = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (m is null) return null;
        var w = await db.Wallets.FirstAsync(x => x.Id == m.DefaultWalletId, ct);
        return ToView(w);
    }

    public async Task<IReadOnlyList<LedgerTransactionView>?> GetTransactionsAsync(Guid memberId, CancellationToken ct)
    {
        var m = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (m is null) return null;

        var txs = await db.LedgerTransactions
            .Where(t => t.WalletId == m.DefaultWalletId)
            .OrderByDescending(t => t.OccurredAt)
            .ToListAsync(ct);

        var accountCodes = await db.LedgerAccounts
            .Where(a => a.ApplicationId == m.ApplicationId)
            .ToDictionaryAsync(a => a.Id, a => a, ct);

        var result = new List<LedgerTransactionView>();
        foreach (var t in txs)
        {
            var postings = await db.LedgerPostings
                .Where(p => p.LedgerTransactionId == t.Id)
                .OrderBy(p => p.Sequence)
                .ToListAsync(ct);

            var pv = postings.Select(p =>
            {
                var acc = accountCodes.GetValueOrDefault(p.LedgerAccountId);
                return new LedgerPostingView(p.Sequence, acc?.AccountCode ?? "", acc?.AccountType.ToString() ?? "",
                    acc?.BalanceBucket?.ToString(), p.DebitAmount, p.CreditAmount);
            }).ToList();

            result.Add(new LedgerTransactionView(t.Id, t.TransactionType.ToString(), t.PointsUnitCode, t.TotalDebit, t.TotalCredit,
                t.SourceEventId, t.SourceSystem, t.SourceTransactionId, t.CampaignId, t.EffectId, t.OccurredAt, pv));
        }
        return result;
    }

    private static WalletView ToView(Wallet w) =>
        new(w.Id, w.MemberId, w.PointsUnitCode, w.ActiveBalance, w.PendingBalance, w.ReservedBalance, w.Version);
}
