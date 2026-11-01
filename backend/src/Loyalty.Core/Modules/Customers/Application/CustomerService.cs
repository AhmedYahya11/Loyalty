using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Members.Application;
using Loyalty.Core.Modules.Rewards.Application;
using Loyalty.Core.Modules.Engagement.Application;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Customers.Application;

/// <summary>
/// The customer-facing self-service surface (P11). Every method is implicitly scoped to the member
/// behind the access token (<see cref="CustomerContext.MemberId"/>) — a customer can only ever read
/// or act on their own data. Reuses the existing admin services, which already run inside the
/// member's application scope established by the token auth branch.
/// </summary>
public sealed class CustomerService(
    IRepository<Loyalty.Core.Modules.Rewards.Domain.Redemption> redemptionsRepo,
    IRepository<Loyalty.Core.Modules.Engagement.Domain.Referral> referralsRepo,
    IRepository<Loyalty.Core.Modules.Raffles.Domain.Raffle> rafflesRepo,
    IRepository<Loyalty.Core.Modules.Raffles.Domain.RaffleEntry> raffleEntriesRepo,
    CustomerContext customer, MemberService members,
    RewardService rewards, RedemptionService redemptions, StampCardService stampCards)
{
    private Guid Me => customer.MemberId;

    public Task<MemberView?> GetMemberAsync(CancellationToken ct) => members.GetAsync(Me, ct);
    public Task<object?> GetProfileAsync(CancellationToken ct) => members.GetValueProfileAsync(Me, ct);
    public Task<IReadOnlyList<LedgerTransactionView>?> GetTransactionsAsync(CancellationToken ct) => members.GetTransactionsAsync(Me, ct);
    public Task<IReadOnlyList<object>?> GetAchievementsAsync(CancellationToken ct) => members.GetAchievementsAsync(Me, ct);
    public Task<IReadOnlyList<object>> GetStampCardsAsync(CancellationToken ct) => stampCards.ProgressAsync(Me, ct);

    /// <summary>Active rewards with an "affordable" flag computed against the member's active balance.</summary>
    public async Task<IReadOnlyList<object>> GetRewardsAsync(CancellationToken ct)
    {
        var wallet = await members.GetWalletAsync(Me, ct);
        var balance = wallet?.ActiveBalance ?? 0;
        var list = await rewards.ListAsync(activeOnly: true, ct);
        return list.Select(r => (object)new
        {
            r.Id, r.Name, r.NameAr, r.Description, r.PointsCost, r.RewardType, r.Value, r.Inventory,
            affordable = balance >= r.PointsCost,
        }).ToList();
    }

    public Task<IReadOnlyList<RedemptionView>> GetRedemptionsAsync(CancellationToken ct) => redemptions.ListMemberAsync(Me, ct);

    public Task<RedemptionView> ReserveAsync(Guid rewardId, CancellationToken ct) => redemptions.ReserveAsync(Me, rewardId, ct);

    /// <summary>Cancel one of the member's own reserved redemptions (ownership enforced).</summary>
    public async Task<RedemptionView?> CancelAsync(Guid redemptionId, CancellationToken ct)
    {
        var owns = await redemptionsRepo.Query().AnyAsync(r => r.Id == redemptionId && r.MemberId == Me, ct);
        if (!owns) return null;
        return await redemptions.CancelAsync(redemptionId, ct);
    }

    public async Task<IReadOnlyList<object>> GetReferralsAsync(CancellationToken ct) =>
        await referralsRepo.Query().Where(r => r.ReferrerMemberId == Me).OrderByDescending(r => r.CreatedAt)
            .Select(r => (object)new { r.ReferralKey, r.ReferrerPointsAwarded, r.CreatedAt }).ToListAsync(ct);

    /// <summary>The member's own entries in each open/drawn raffle (P15).</summary>
    public async Task<IReadOnlyList<object>> GetRafflesAsync(CancellationToken ct) =>
        await (from r in rafflesRepo.Query()
               where r.Status != Modules.Raffles.Domain.RaffleStatus.Cancelled
               orderby r.CreatedAt descending
               select (object)new
               {
                   raffleId = r.Id, name = r.Name, nameAr = r.NameAr, prizePoints = r.PrizePoints,
                   prizeDescription = r.PrizeDescription, status = r.Status.ToString(), drawAtUtc = r.DrawAtUtc,
                   myEntries = raffleEntriesRepo.Query().Where(e => e.RaffleId == r.Id && e.MemberId == Me).Sum(e => (int?)e.Count) ?? 0,
                   won = r.WinnerMemberId == Me,
               }).ToListAsync(ct);
}
