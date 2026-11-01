using Loyalty.Core.Modules.Customers.Application;
using Microsoft.AspNetCore.Mvc;

namespace Loyalty.Api.Controllers;

/// <summary>Customer-facing app (P11): member-token auth, scoped to the caller's own data only.</summary>
[Tags("Customer app")]
[Route("api/v1/customer")]
public sealed class CustomerAppController(CustomerService svc) : LoyaltyControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var m = await svc.GetMemberAsync(ct);
        if (m is null) return NotFound();
        return Ok(new { member = m, profile = await svc.GetProfileAsync(ct) });
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(CancellationToken ct) =>
        await svc.GetTransactionsAsync(ct) is { } v ? Ok(new { items = v }) : NotFound();

    [HttpGet("rewards")]
    public async Task<IActionResult> Rewards(CancellationToken ct) => Ok(await svc.GetRewardsAsync(ct));

    [HttpGet("redemptions")]
    public async Task<IActionResult> Redemptions(CancellationToken ct) => Ok(new { items = await svc.GetRedemptionsAsync(ct) });

    [HttpPost("redemptions")]
    public async Task<IActionResult> Reserve([FromBody] RedeemRequest req, CancellationToken ct) => Created201(await svc.ReserveAsync(req.RewardId, ct));

    [HttpPost("redemptions/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => OkOrNotFound(await svc.CancelAsync(id, ct));

    [HttpGet("stamp-cards")]
    public async Task<IActionResult> StampCards(CancellationToken ct) => Ok(new { items = await svc.GetStampCardsAsync(ct) });

    [HttpGet("achievements")]
    public async Task<IActionResult> Achievements(CancellationToken ct) =>
        await svc.GetAchievementsAsync(ct) is { } v ? Ok(new { items = v }) : NotFound();

    [HttpGet("referrals")]
    public async Task<IActionResult> Referrals(CancellationToken ct) => Ok(new { items = await svc.GetReferralsAsync(ct) });

    [HttpGet("raffles")]
    public async Task<IActionResult> Raffles(CancellationToken ct) => Ok(new { items = await svc.GetRafflesAsync(ct) });
}
