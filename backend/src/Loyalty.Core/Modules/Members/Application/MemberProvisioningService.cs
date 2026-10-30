using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Members.Domain;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Modules.Wallets.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Members.Application;

/// <summary>
/// Atomically provisions a member, its mandatory default wallet, the three credit-normal
/// member ledger accounts, and any missing application-local clearing accounts for the unit
/// (MVP spec §10, §15.5). Idempotent via the unique (ApplicationId, ExternalCustomerId)
/// index. Does NOT call SaveChanges — the caller owns the transaction.
/// </summary>
public sealed class MemberProvisioningService(LoyaltyDbContext db, LedgerService ledger, IClock clock)
{
    public async Task<(Member Member, Wallet Wallet)> EnsureAsync(
        Guid applicationId, string externalCustomerId, string pointsUnitCode, CancellationToken ct)
    {
        externalCustomerId = externalCustomerId.Trim();
        var existing = await db.Members
            .FirstOrDefaultAsync(m => m.ApplicationId == applicationId && m.ExternalCustomerId == externalCustomerId, ct);
        if (existing is not null)
        {
            var w = await db.Wallets.FirstAsync(x => x.Id == existing.DefaultWalletId, ct);
            return (existing, w);
        }

        var now = clock.UtcNow;
        var memberId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        var wallet = Wallet.Create(walletId, applicationId, memberId, pointsUnitCode);
        var member = Member.Create(memberId, applicationId, externalCustomerId, walletId, now);

        db.Wallets.Add(wallet);
        db.Members.Add(member);
        ledger.CreateMemberAccounts(applicationId, walletId, pointsUnitCode);
        await ledger.EnsureClearingAccountsAsync(applicationId, pointsUnitCode, ct);

        return (member, wallet);
    }
}
