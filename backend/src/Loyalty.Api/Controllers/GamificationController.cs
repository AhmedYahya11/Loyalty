using Loyalty.Core.Modules.Engagement.Application;
using Loyalty.Core.Modules.Gamification.Application;
using Loyalty.Core.Modules.Raffles.Application;
using Microsoft.AspNetCore.Mvc;

namespace Loyalty.Api.Controllers;

/// <summary>Achievements / badges catalog.</summary>
[Tags("Achievements")]
[Route("api/v1/achievements")]
public sealed class AchievementsController(AchievementService svc) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAchievementInput req, CancellationToken ct) => Created201(await svc.CreateAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAchievementInput req, CancellationToken ct) => OkOrNotFound(await svc.UpdateAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => NoContentIf(await svc.DeleteAsync(id, ct));
}

/// <summary>Time-boxed challenges and the leaderboard.</summary>
[Tags("Challenges")]
[Route("api/v1/challenges")]
public sealed class ChallengesController(ChallengeService svc, LeaderboardService leaderboard) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChallengeInput req, CancellationToken ct) => Created201(await svc.CreateAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => NoContentIf(await svc.DeleteAsync(id, ct));

    [HttpGet("/api/v1/leaderboard")]
    public async Task<IActionResult> Leaderboard([FromQuery] string? metric, [FromQuery] int? limit, CancellationToken ct) =>
        Ok(new { metric = metric ?? "PointsEarned", items = await leaderboard.TopAsync(metric ?? "PointsEarned", limit ?? 10, ct) });
}

/// <summary>Digital stamp (punch) cards.</summary>
[Tags("Stamp cards")]
[Route("api/v1/stamp-cards")]
public sealed class StampCardsController(StampCardService svc) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStampCardInput req, CancellationToken ct) => Created201(await svc.CreateAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStampCardInput req, CancellationToken ct) => OkOrNotFound(await svc.UpdateAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => NoContentIf(await svc.DeleteAsync(id, ct));
}

/// <summary>Prize draws (raffles): entries + weighted-random draw.</summary>
[Tags("Raffles")]
[Route("api/v1/raffles")]
public sealed class RafflesController(RaffleService svc) : LoyaltyControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRaffleInput req, CancellationToken ct) => Created201(await svc.CreateAsync(req, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpGet("{id:guid}/entries")]
    public async Task<IActionResult> Entries(Guid id, CancellationToken ct) => Ok(new { items = await svc.EntriesAsync(id, ct) });

    [HttpPost("{id:guid}/draw")]
    public async Task<IActionResult> Draw(Guid id, CancellationToken ct) => OkOrNotFound(await svc.DrawAsync(id, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => NoContentIf(await svc.DeleteAsync(id, ct));
}
