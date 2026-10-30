using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Cvm.Domain;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Members.Application;

/// <summary>
/// Atomically provisions a member, its mandatory default wallet, the three credit-normal
/// member ledger accounts, any missing application-local clearing accounts, and the member's
/// initial MemberValueProfile (MVP spec §10, §11.2, §15.5). Idempotent via the unique
/// (ApplicationId, ExternalCustomerId) index. Does NOT call SaveChanges — the caller owns the
/// transaction.
/// </summary>
public sealed class MemberProvisioningService(LoyaltyDbContext db, LedgerService ledger, IClock clock)
{
    public async Task<(Member Member, Wallet Wallet)> EnsureAsync(
        Guid applicationId, string externalCustomerId, string pointsUnitCode, string baseCurrency, CancellationToken ct,
        DateOnly? birthDate = null)
    {
        externalCustomerId = externalCustomerId.Trim();
        var existing = await db.Members
            .FirstOrDefaultAsync(m => m.ApplicationId == applicationId && m.ExternalCustomerId == externalCustomerId, ct);
        if (existing is not null)
        {
            var w = await db.Wallets.FirstAsync(x => x.Id == existing.DefaultWalletId, ct);
            if (birthDate is not null && existing.BirthDate is null) existing.SetBirthDate(birthDate, clock.UtcNow);
            return (existing, w);
        }

        var now = clock.UtcNow;
        var memberId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        var wallet = Wallet.Create(walletId, applicationId, memberId, pointsUnitCode);
        var member = Member.Create(memberId, applicationId, externalCustomerId, walletId, now,
            referralCode: GenerateReferralCode(memberId), birthDate: birthDate);
        var profile = MemberValueProfile.Create(applicationId, memberId, baseCurrency, now);

        db.Wallets.Add(wallet);
        db.Members.Add(member);
        db.ValueProfiles.Add(profile);
        ledger.CreateMemberAccounts(applicationId, walletId, pointsUnitCode);
        await ledger.EnsureClearingAccountsAsync(applicationId, pointsUnitCode, ct);

        return (member, wallet);
    }

    /// <summary>Deterministic, human-shareable referral code derived from the member id (P10).</summary>
    private static string GenerateReferralCode(Guid memberId) =>
        "REF-" + memberId.ToString("N")[..8].ToUpperInvariant();
}
