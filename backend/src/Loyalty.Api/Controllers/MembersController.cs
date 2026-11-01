using Loyalty.Core.Modules.Engagement.Application;
using Loyalty.Core.Modules.Members.Application;
using Loyalty.Core.Modules.Rewards.Application;
using Loyalty.Core.Modules.Wallets.Application;
using Loyalty.Core.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Api.Controllers;

/// <summary>Members, their wallet/ledger, and member-scoped resources (redemptions, achievements, stamp cards, referrals).</summary>
[Tags("Members")]
[Route("api/v1/members")]
public sealed class MembersController(
    MemberService members,
    MemberTokenService tokens,
    RedemptionService redemptions,
    ReconciliationService reconciliation,
    StampCardService stampCards,
    LoyaltyDbContext db) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMemberRequest req, CancellationToken ct) =>
        Created201(await members.CreateAsync(req.ExternalCustomerId, ct, req.BirthDate));

    [HttpPut("{id:guid}/birthdate")]
    public async Task<IActionResult> SetBirthDate(Guid id, [FromBody] SetBirthDateRequest req, CancellationToken ct) =>
        OkOrNotFound(await members.SetBirthDateAsync(id, req.BirthDate, ct));

    // mint a member access token for the customer-facing app (P11)
    [HttpPost("{id:guid}/access-token")]
    public async Task<IActionResult> IssueToken(Guid id, [FromBody] IssueTokenRequest? req, CancellationToken ct)
    {
        var ttlDays = req?.TtlDays is int n && n > 0 ? n : 30;
        var t = await tokens.IssueAsync(id, TimeSpan.FromDays(ttlDays), ct);
        return t is null ? NotFound() : Ok(new { token = t.Token, expiresAt = t.ExpiresAt });
    }

    // revoke every access token for a member (invalidates any shared card link/QR) (P11)
    [HttpDelete("{id:guid}/access-tokens")]
    public async Task<IActionResult> RevokeTokens(Guid id, CancellationToken ct)
    {
        var revoked = await tokens.RevokeAllAsync(id, ct);
        return revoked is null ? NotFound() : Ok(new { revoked });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => OkOrNotFound(await members.GetAsync(id, ct));

    [HttpGet("by-external-id/{externalId}")]
    public async Task<IActionResult> GetByExternalId(string externalId, CancellationToken ct) =>
        OkOrNotFound(await members.GetByExternalIdAsync(externalId, ct));

    [HttpGet("{id:guid}/value-profile")]
    public async Task<IActionResult> ValueProfile(Guid id, CancellationToken ct) =>
        OkOrNotFound(await members.GetValueProfileAsync(id, ct));

    [HttpGet("{id:guid}/segments")]
    public async Task<IActionResult> Segments(Guid id, CancellationToken ct) =>
        OkOrNotFound(await members.GetSegmentsAsync(id, ct));

    [HttpGet("{id:guid}/wallet")]
    public async Task<IActionResult> Wallet(Guid id, CancellationToken ct) =>
        OkOrNotFound(await members.GetWalletAsync(id, ct));

    [HttpGet("{id:guid}/wallet/transactions")]
    public async Task<IActionResult> Transactions(Guid id, CancellationToken ct) =>
        await members.GetTransactionsAsync(id, ct) is { } v ? Ok(new { items = v }) : NotFound();

    [HttpPost("{id:guid}/reconcile")]
    public async Task<IActionResult> Reconcile(Guid id, [FromQuery] bool? repair, CancellationToken ct) =>
        OkOrNotFound(await reconciliation.ReconcileMemberAsync(id, repair ?? false, ct));

    // ----- redemptions (reserve → confirm → cancel) -----
    [HttpPost("{id:guid}/redemptions")]
    public async Task<IActionResult> Reserve(Guid id, [FromBody] RedeemRequest req, CancellationToken ct) =>
        Created201(await redemptions.ReserveAsync(id, req.RewardId, ct));

    [HttpPost("{id:guid}/grant-reward")]
    public async Task<IActionResult> GrantReward(Guid id, [FromBody] RedeemRequest req, CancellationToken ct) =>
        await redemptions.GrantStandaloneAsync(id, req.RewardId, ct) is { } v ? Created201(v) : NotFound();

    [HttpGet("{id:guid}/redemptions")]
    public async Task<IActionResult> MemberRedemptions(Guid id, CancellationToken ct) =>
        Ok(new { items = await redemptions.ListMemberAsync(id, ct) });

    // ----- gamification + engagement (member views) -----
    [HttpGet("{id:guid}/achievements")]
    public async Task<IActionResult> Achievements(Guid id, CancellationToken ct) =>
        await members.GetAchievementsAsync(id, ct) is { } v ? Ok(new { items = v }) : NotFound();

    [HttpGet("{id:guid}/stamp-cards")]
    public async Task<IActionResult> StampCards(Guid id, CancellationToken ct) =>
        Ok(new { items = await stampCards.ProgressAsync(id, ct) });

    [HttpGet("{id:guid}/referrals")]
    public async Task<IActionResult> Referrals(Guid id, CancellationToken ct) =>
        Ok(new { items = await db.Referrals.Where(r => r.ReferrerMemberId == id).OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.ReferralKey, r.RefereeMemberId, r.ReferrerPointsAwarded, r.CreatedAt }).ToListAsync(ct) });
}
