using Loyalty.Core.Modules.Campaigns.Application;
using Loyalty.Core.Modules.Cvm.Application;
using Loyalty.Core.Modules.Rewards.Application;
using Loyalty.Core.Modules.Tiers.Application;
using Microsoft.AspNetCore.Mvc;

namespace Loyalty.Api.Controllers;

/// <summary>Campaign engine: create, validate, simulate, activate, pause.</summary>
[Tags("Campaigns")]
[Route("api/v1/campaigns")]
public sealed class CampaignsController(CampaignService svc) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCampaignInput req, CancellationToken ct) => Created201(await svc.CreateAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => OkOrNotFound(await svc.GetAsync(id, ct));

    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct) => OkOrNotFound(await svc.ValidateAsync(id, ct));

    [HttpPost("{id:guid}/simulate")]
    public async Task<IActionResult> Simulate(Guid id, [FromBody] SimulateInput req, CancellationToken ct) => OkOrNotFound(await svc.SimulateAsync(id, req, ct));

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) => OkOrNotFound(await svc.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct) => OkOrNotFound(await svc.PauseAsync(id, ct));
}

/// <summary>Rule-based CVM segments over the value profile.</summary>
[Tags("Segments")]
[Route("api/v1/segments")]
public sealed class SegmentsController(SegmentService svc) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSegmentInput req, CancellationToken ct) => Created201(await svc.CreateAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => OkOrNotFound(await svc.GetAsync(id, ct));

    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct) => OkOrNotFound(await svc.ValidateAsync(id, ct));

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) => OkOrNotFound(await svc.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct) => OkOrNotFound(await svc.PauseAsync(id, ct));
}

/// <summary>Membership tiers (Bronze → Platinum) and their earn-multiplier benefits.</summary>
[Tags("Tiers")]
[Route("api/v1/tiers")]
public sealed class TiersController(TierService svc) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTierInput req, CancellationToken ct) => Created201(await svc.CreateAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => OkOrNotFound(await svc.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTierInput req, CancellationToken ct) => OkOrNotFound(await svc.UpdateAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => NoContentIf(await svc.DeleteAsync(id, ct));
}

/// <summary>Rewards catalog (redeemable for points).</summary>
[Tags("Rewards")]
[Route("api/v1/rewards")]
public sealed class RewardsController(RewardService svc) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRewardInput req, CancellationToken ct) => Created201(await svc.CreateAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? activeOnly, CancellationToken ct) => Ok(await svc.ListAsync(activeOnly ?? false, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => OkOrNotFound(await svc.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRewardInput req, CancellationToken ct) => OkOrNotFound(await svc.UpdateAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => NoContentIf(await svc.DeleteAsync(id, ct));
}

/// <summary>Redemption coupons: look up by code, confirm (by id or code), and cancel.</summary>
[Tags("Redemptions")]
[Route("api/v1/redemptions")]
public sealed class RedemptionsController(RedemptionService svc) : LoyaltyControllerBase
{
    [HttpGet("by-code/{code}")]
    public async Task<IActionResult> GetByCode(string code, CancellationToken ct) => OkOrNotFound(await svc.GetByCodeAsync(code, ct));

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct) => OkOrNotFound(await svc.ConfirmAsync(id, ct));

    [HttpPost("confirm-by-code")]
    public async Task<IActionResult> ConfirmByCode([FromBody] ConfirmByCodeRequest req, CancellationToken ct) => OkOrNotFound(await svc.ConfirmByCodeAsync(req.CouponCode, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => OkOrNotFound(await svc.CancelAsync(id, ct));
}
