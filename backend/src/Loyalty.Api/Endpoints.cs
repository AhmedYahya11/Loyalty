using System.Text.Json;
using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Accounts.Application;
using Loyalty.Core.Modules.Applications.Application;
using Loyalty.Core.Modules.Campaigns.Application;
using Loyalty.Core.Modules.Integration.Application;
using Loyalty.Core.Modules.Integration.Contracts;
using Loyalty.Core.Modules.Members.Application;
using Loyalty.Core.Modules.Settings.Application;
using Loyalty.Core.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Api;

// Request DTOs (bilingual where user-facing)
public sealed record BootstrapRequest(string Name, string? NameAr);
public sealed record CreateApplicationRequest(string Name, string? NameAr, Guid? ParentId);
public sealed record SetStatusRequest(bool IsActive);
public sealed record CreateAccountRequest(string AccountName, DateTimeOffset? ExpiresAt);
public sealed record CreateMemberRequest(string ExternalCustomerId, DateOnly? BirthDate);
public sealed record SetBirthDateRequest(DateOnly? BirthDate);
public sealed record IssueTokenRequest(int? TtlDays);
public sealed record LoginRequest(string Username, string Password);
public sealed record SetRoleRequest(string Role);
public sealed record RedeemRequest(Guid RewardId);
public sealed record ConfirmByCodeRequest(string CouponCode);

public static class Endpoints
{
    public static void MapLoyaltyEndpoints(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1");

        // ---------- bootstrap (one-time, guarded by X-Bootstrap-Token) ----------
        v1.MapPost("/bootstrap", async (BootstrapRequest req, HttpContext ctx, IConfiguration cfg,
            ApplicationService apps, AccountService accounts, Loyalty.Core.Modules.Identity.Application.AdminIdentityService identity, CancellationToken ct) =>
        {
            var token = cfg["Bootstrap:Token"];
            if (string.IsNullOrEmpty(token) || ctx.Request.Headers["X-Bootstrap-Token"].ToString() != token)
                return Results.Json(new { code = "invalid_credential", message = "Invalid bootstrap token." }, statusCode: 401);

            var (root, created) = await apps.BootstrapRootAsync(req.Name, req.NameAr, ct);
            AccountWithKey? key = created ? await accounts.CreateAsync(root.Id, "root-admin", null, ct) : null;
            // seed the first SuperAdmin against the root (no-op if any admin user already exists)
            var suUser = Environment.GetEnvironmentVariable("SUPERADMIN_USERNAME") ?? "superadmin";
            var suPass = Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD") ?? "ChangeMe!123";
            await identity.EnsureSuperAdminAsync(root.Id, suUser, suPass, ct);
            return Results.Json(new { application = root, created, rootAccount = key }, statusCode: created ? 201 : 200);
        });

        // ---------- applications ----------
        // current authenticated application (resolved from the API key) — used by the dashboard
        v1.MapGet("/me", (Loyalty.Core.BuildingBlocks.ApplicationContext scope) =>
            Results.Ok(new { applicationId = scope.ApplicationId }));

        // ---------- dashboard identity + RBAC (P17) ----------
        v1.MapPost("/admin/login", async (LoginRequest req, Loyalty.Core.Modules.Identity.Application.AdminIdentityService svc, CancellationToken ct) =>
            await svc.LoginAsync(req.Username, req.Password, ct) is { } r
                ? Results.Ok(new { token = r.Token, expiresAt = r.ExpiresAt, username = r.Username, role = r.Role, applicationId = r.ApplicationId })
                : Results.Json(new { code = "invalid_credential", message = "Invalid username or password." }, statusCode: 401));
        v1.MapGet("/admin/me", (Loyalty.Core.BuildingBlocks.AdminContext me) =>
            me.IsEstablished
                ? Results.Ok(new { username = me.Username, role = me.Role.ToString(), applicationId = me.ApplicationId, canManageUsers = me.CanManageUsers })
                : Results.Json(new { code = "invalid_credential", message = "Not logged in as an admin." }, statusCode: 401));
        v1.MapPost("/admin/logout", async (HttpContext ctx, Loyalty.Core.Modules.Identity.Application.AdminIdentityService svc, CancellationToken ct) =>
        {
            var h = ctx.Request.Headers.Authorization.ToString();
            await svc.RevokeAsync(h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h["Bearer ".Length..].Trim() : h, ct);
            return Results.NoContent();
        });
        v1.MapGet("/admin/roles", (Loyalty.Core.BuildingBlocks.AdminContext me) =>
            me.IsEstablished ? Results.Ok(new[]
            {
                new { role = "Viewer", description = "Read-only access to all data." },
                new { role = "Manager", description = "Manage loyalty config (campaigns, rewards, tiers, …)." },
                new { role = "Admin", description = "Everything in the tenant, including managing users." },
                new { role = "SuperAdmin", description = "Full access across all tenants, including admins." },
            }) : Results.Json(new { code = "invalid_credential", message = "Not logged in." }, statusCode: 401));
        v1.MapGet("/admin/users", async (Loyalty.Core.BuildingBlocks.AdminContext me, Loyalty.Core.Modules.Identity.Application.AdminIdentityService svc, CancellationToken ct) =>
            me.CanManageUsers ? Results.Ok(await svc.ListUsersAsync(me, ct)) : Results.Json(new { code = "forbidden", message = "Admins only." }, statusCode: 403));
        v1.MapPost("/admin/users", async (Loyalty.Core.Modules.Identity.Application.CreateAdminUserInput req, Loyalty.Core.BuildingBlocks.AdminContext me, Loyalty.Core.Modules.Identity.Application.AdminIdentityService svc, CancellationToken ct) =>
            me.CanManageUsers ? Results.Json(await svc.CreateUserAsync(me, req, ct), statusCode: 201) : Results.Json(new { code = "forbidden", message = "Admins only." }, statusCode: 403));
        v1.MapPut("/admin/users/{id:guid}/role", async (Guid id, SetRoleRequest req, Loyalty.Core.BuildingBlocks.AdminContext me, Loyalty.Core.Modules.Identity.Application.AdminIdentityService svc, CancellationToken ct) =>
            !me.CanManageUsers ? Results.Json(new { code = "forbidden", message = "Admins only." }, statusCode: 403)
                : await svc.SetRoleAsync(me, id, req.Role, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        v1.MapPost("/admin/users/{id:guid}/deactivate", async (Guid id, Loyalty.Core.BuildingBlocks.AdminContext me, Loyalty.Core.Modules.Identity.Application.AdminIdentityService svc, CancellationToken ct) =>
            !me.CanManageUsers ? Results.Json(new { code = "forbidden", message = "Admins only." }, statusCode: 403)
                : await svc.DeactivateAsync(me, id, ct) ? Results.NoContent() : Results.NotFound());

        // ---------- customers (browsable member list) ----------
        v1.MapGet("/customers", async (string? search, int? limit, MemberService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.ListAsync(search, limit ?? 100, ct) }));

        var apps = v1.MapGroup("/applications");
        apps.MapPost("", async (CreateApplicationRequest req, ApplicationService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req.Name, req.NameAr, req.ParentId, ct), statusCode: 201));
        apps.MapGet("", async (ApplicationService svc, CancellationToken ct) => Results.Ok(await svc.ListVisibleAsync(ct)));
        apps.MapGet("/{id:guid}", async (Guid id, ApplicationService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        apps.MapPatch("/{id:guid}/status", async (Guid id, SetStatusRequest req, ApplicationService svc, CancellationToken ct) =>
        { await svc.SetStatusAsync(id, req.IsActive, ct); return Results.NoContent(); });

        // ---------- accounts ----------
        apps.MapPost("/{appId:guid}/accounts", async (Guid appId, CreateAccountRequest req, AccountService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(appId, req.AccountName, req.ExpiresAt, ct), statusCode: 201));
        apps.MapGet("/{appId:guid}/accounts", async (Guid appId, AccountService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(appId, ct)));
        var accts = v1.MapGroup("/accounts");
        accts.MapPatch("/{id:guid}/status", async (Guid id, SetStatusRequest req, AccountService svc, CancellationToken ct) =>
        { await svc.SetStatusAsync(id, req.IsActive, ct); return Results.NoContent(); });
        accts.MapPost("/{id:guid}/rotate-key", async (Guid id, AccountService svc, CancellationToken ct) =>
            Results.Ok(await svc.RotateKeyAsync(id, ct)));
        accts.MapPost("/{id:guid}/revoke", async (Guid id, AccountService svc, CancellationToken ct) =>
        { await svc.RevokeAsync(id, ct); return Results.NoContent(); });

        // ---------- loyalty settings ----------
        apps.MapPut("/{appId:guid}/loyalty-settings", async (Guid appId, PutLoyaltySettingsInput req, LoyaltySettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.PutAsync(appId, req, ct)));
        apps.MapGet("/{appId:guid}/loyalty-settings", async (Guid appId, LoyaltySettingsService svc, CancellationToken ct) =>
            await svc.GetEffectiveAsync(appId, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        apps.MapDelete("/{appId:guid}/loyalty-settings", async (Guid appId, LoyaltySettingsService svc, CancellationToken ct) =>
            await svc.DeleteAsync(appId, ct) ? Results.NoContent() : Results.NotFound());

        // ---------- CVM settings ----------
        apps.MapPut("/{appId:guid}/cvm-settings", async (Guid appId, Loyalty.Core.Modules.Cvm.Domain.CvmFields req, Loyalty.Core.Modules.Cvm.Application.CvmSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.PutAsync(appId, req, ct)));
        apps.MapGet("/{appId:guid}/cvm-settings", async (Guid appId, Loyalty.Core.Modules.Cvm.Application.CvmSettingsService svc, CancellationToken ct) =>
            await svc.GetEffectiveAsync(appId, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        apps.MapDelete("/{appId:guid}/cvm-settings", async (Guid appId, Loyalty.Core.Modules.Cvm.Application.CvmSettingsService svc, CancellationToken ct) =>
            await svc.DeleteAsync(appId, ct) ? Results.NoContent() : Results.NotFound());

        // ---------- members + wallet ----------
        var members = v1.MapGroup("/members");
        members.MapPost("", async (CreateMemberRequest req, MemberService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req.ExternalCustomerId, ct, req.BirthDate), statusCode: 201));
        members.MapPut("/{id:guid}/birthdate", async (Guid id, SetBirthDateRequest req, MemberService svc, CancellationToken ct) =>
            await svc.SetBirthDateAsync(id, req.BirthDate, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        // mint a member access token for the customer-facing app (P11)
        members.MapPost("/{id:guid}/access-token", async (Guid id, IssueTokenRequest? req, MemberTokenService svc, CancellationToken ct) =>
        {
            var ttlDays = req?.TtlDays is int n && n > 0 ? n : 30;
            var t = await svc.IssueAsync(id, TimeSpan.FromDays(ttlDays), ct);
            return t is null ? Results.NotFound() : Results.Ok(new { token = t.Token, expiresAt = t.ExpiresAt });
        });
        members.MapGet("/{id:guid}", async (Guid id, MemberService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        members.MapGet("/by-external-id/{externalId}", async (string externalId, MemberService svc, CancellationToken ct) =>
            await svc.GetByExternalIdAsync(externalId, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        members.MapGet("/{id:guid}/value-profile", async (Guid id, MemberService svc, CancellationToken ct) =>
            await svc.GetValueProfileAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        members.MapGet("/{id:guid}/segments", async (Guid id, MemberService svc, CancellationToken ct) =>
            await svc.GetSegmentsAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        members.MapGet("/{id:guid}/wallet", async (Guid id, MemberService svc, CancellationToken ct) =>
            await svc.GetWalletAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        members.MapGet("/{id:guid}/wallet/transactions", async (Guid id, MemberService svc, CancellationToken ct) =>
            await svc.GetTransactionsAsync(id, ct) is { } v ? Results.Ok(new { items = v }) : Results.NotFound());
        members.MapPost("/{id:guid}/reconcile", async (Guid id, bool? repair, Loyalty.Core.Modules.Wallets.Application.ReconciliationService svc, CancellationToken ct) =>
            await svc.ReconcileMemberAsync(id, repair ?? false, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        // ---------- campaigns ----------
        var campaigns = v1.MapGroup("/campaigns");
        campaigns.MapPost("", async (CreateCampaignInput req, CampaignService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req, ct), statusCode: 201));
        campaigns.MapGet("", async (CampaignService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        campaigns.MapGet("/{id:guid}", async (Guid id, CampaignService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        campaigns.MapPost("/{id:guid}/validate", async (Guid id, CampaignService svc, CancellationToken ct) =>
            await svc.ValidateAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        campaigns.MapPost("/{id:guid}/simulate", async (Guid id, SimulateInput req, CampaignService svc, CancellationToken ct) =>
            await svc.SimulateAsync(id, req, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        campaigns.MapPost("/{id:guid}/activate", async (Guid id, CampaignService svc, CancellationToken ct) =>
            await svc.ActivateAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        campaigns.MapPost("/{id:guid}/pause", async (Guid id, CampaignService svc, CancellationToken ct) =>
            await svc.PauseAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        // ---------- segments (CVM) ----------
        var segments = v1.MapGroup("/segments");
        segments.MapPost("", async (Loyalty.Core.Modules.Cvm.Application.CreateSegmentInput req, Loyalty.Core.Modules.Cvm.Application.SegmentService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req, ct), statusCode: 201));
        segments.MapGet("", async (Loyalty.Core.Modules.Cvm.Application.SegmentService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        segments.MapGet("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Cvm.Application.SegmentService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        segments.MapPost("/{id:guid}/validate", async (Guid id, Loyalty.Core.Modules.Cvm.Application.SegmentService svc, CancellationToken ct) =>
            await svc.ValidateAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        segments.MapPost("/{id:guid}/activate", async (Guid id, Loyalty.Core.Modules.Cvm.Application.SegmentService svc, CancellationToken ct) =>
            await svc.ActivateAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        segments.MapPost("/{id:guid}/pause", async (Guid id, Loyalty.Core.Modules.Cvm.Application.SegmentService svc, CancellationToken ct) =>
            await svc.PauseAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        // ---------- tiers ----------
        var tiers = v1.MapGroup("/tiers");
        tiers.MapPost("", async (Loyalty.Core.Modules.Tiers.Application.CreateTierInput req, Loyalty.Core.Modules.Tiers.Application.TierService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req, ct), statusCode: 201));
        tiers.MapGet("", async (Loyalty.Core.Modules.Tiers.Application.TierService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        tiers.MapGet("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Tiers.Application.TierService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        tiers.MapPut("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Tiers.Application.UpdateTierInput req, Loyalty.Core.Modules.Tiers.Application.TierService svc, CancellationToken ct) =>
            await svc.UpdateAsync(id, req, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        tiers.MapDelete("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Tiers.Application.TierService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // ---------- rewards catalog ----------
        var rewards = v1.MapGroup("/rewards");
        rewards.MapPost("", async (Loyalty.Core.Modules.Rewards.Application.CreateRewardInput req, Loyalty.Core.Modules.Rewards.Application.RewardService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req, ct), statusCode: 201));
        rewards.MapGet("", async (bool? activeOnly, Loyalty.Core.Modules.Rewards.Application.RewardService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(activeOnly ?? false, ct)));
        rewards.MapGet("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Rewards.Application.RewardService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        rewards.MapPut("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Rewards.Application.UpdateRewardInput req, Loyalty.Core.Modules.Rewards.Application.RewardService svc, CancellationToken ct) =>
            await svc.UpdateAsync(id, req, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        rewards.MapDelete("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Rewards.Application.RewardService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // ---------- redemptions (reserve → confirm → cancel) ----------
        members.MapPost("/{id:guid}/redemptions", async (Guid id, RedeemRequest req, Loyalty.Core.Modules.Rewards.Application.RedemptionService svc, CancellationToken ct) =>
            Results.Json(await svc.ReserveAsync(id, req.RewardId, ct), statusCode: 201));
        members.MapPost("/{id:guid}/grant-reward", async (Guid id, RedeemRequest req, Loyalty.Core.Modules.Rewards.Application.RedemptionService svc, CancellationToken ct) =>
            await svc.GrantStandaloneAsync(id, req.RewardId, ct) is { } v ? Results.Json(v, statusCode: 201) : Results.NotFound());
        members.MapGet("/{id:guid}/redemptions", async (Guid id, Loyalty.Core.Modules.Rewards.Application.RedemptionService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.ListMemberAsync(id, ct) }));
        var redemptions = v1.MapGroup("/redemptions");
        redemptions.MapGet("/by-code/{code}", async (string code, Loyalty.Core.Modules.Rewards.Application.RedemptionService svc, CancellationToken ct) =>
            await svc.GetByCodeAsync(code, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        redemptions.MapPost("/{id:guid}/confirm", async (Guid id, Loyalty.Core.Modules.Rewards.Application.RedemptionService svc, CancellationToken ct) =>
            await svc.ConfirmAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        redemptions.MapPost("/confirm-by-code", async (ConfirmByCodeRequest req, Loyalty.Core.Modules.Rewards.Application.RedemptionService svc, CancellationToken ct) =>
            await svc.ConfirmByCodeAsync(req.CouponCode, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        redemptions.MapPost("/{id:guid}/cancel", async (Guid id, Loyalty.Core.Modules.Rewards.Application.RedemptionService svc, CancellationToken ct) =>
            await svc.CancelAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        // ---------- gamification: achievements ----------
        var achievements = v1.MapGroup("/achievements");
        achievements.MapPost("", async (Loyalty.Core.Modules.Gamification.Application.CreateAchievementInput req, Loyalty.Core.Modules.Gamification.Application.AchievementService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req, ct), statusCode: 201));
        achievements.MapGet("", async (Loyalty.Core.Modules.Gamification.Application.AchievementService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        achievements.MapPut("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Gamification.Application.UpdateAchievementInput req, Loyalty.Core.Modules.Gamification.Application.AchievementService svc, CancellationToken ct) =>
            await svc.UpdateAsync(id, req, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        achievements.MapDelete("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Gamification.Application.AchievementService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
        members.MapGet("/{id:guid}/achievements", async (Guid id, MemberService svc, CancellationToken ct) =>
            await svc.GetAchievementsAsync(id, ct) is { } v ? Results.Ok(new { items = v }) : Results.NotFound());

        // ---------- gamification: challenges + leaderboard ----------
        var challenges = v1.MapGroup("/challenges");
        challenges.MapPost("", async (Loyalty.Core.Modules.Gamification.Application.CreateChallengeInput req, Loyalty.Core.Modules.Gamification.Application.ChallengeService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req, ct), statusCode: 201));
        challenges.MapGet("", async (Loyalty.Core.Modules.Gamification.Application.ChallengeService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        challenges.MapDelete("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Gamification.Application.ChallengeService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
        v1.MapGet("/leaderboard", async (string? metric, int? limit, Loyalty.Core.Modules.Gamification.Application.LeaderboardService svc, CancellationToken ct) =>
            Results.Ok(new { metric = metric ?? "PointsEarned", items = await svc.TopAsync(metric ?? "PointsEarned", limit ?? 10, ct) }));

        // ---------- engagement (P10): referral program + stamp cards + birthday ----------
        apps.MapPut("/{appId:guid}/engagement-settings", async (Guid appId, Loyalty.Core.Modules.Engagement.Application.EngagementSettingsInput req, Loyalty.Core.Modules.Engagement.Application.EngagementSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpsertAsync(appId, req, ct)));
        apps.MapGet("/{appId:guid}/engagement-settings", async (Guid appId, Loyalty.Core.Modules.Engagement.Application.EngagementSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(appId, ct)));

        var stampCards = v1.MapGroup("/stamp-cards");
        stampCards.MapPost("", async (Loyalty.Core.Modules.Engagement.Application.CreateStampCardInput req, Loyalty.Core.Modules.Engagement.Application.StampCardService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req, ct), statusCode: 201));
        stampCards.MapGet("", async (Loyalty.Core.Modules.Engagement.Application.StampCardService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        stampCards.MapPut("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Engagement.Application.UpdateStampCardInput req, Loyalty.Core.Modules.Engagement.Application.StampCardService svc, CancellationToken ct) =>
            await svc.UpdateAsync(id, req, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        stampCards.MapDelete("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Engagement.Application.StampCardService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
        members.MapGet("/{id:guid}/stamp-cards", async (Guid id, Loyalty.Core.Modules.Engagement.Application.StampCardService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.ProgressAsync(id, ct) }));
        members.MapGet("/{id:guid}/referrals", async (Guid id, LoyaltyDbContext db, CancellationToken ct) =>
            Results.Ok(new { items = await db.Referrals.Where(r => r.ReferrerMemberId == id).OrderByDescending(r => r.CreatedAt)
                .Select(r => new { r.ReferralKey, r.RefereeMemberId, r.ReferrerPointsAwarded, r.CreatedAt }).ToListAsync(ct) }));

        // ---------- raffles / prize draws (P15) ----------
        var raffles = v1.MapGroup("/raffles");
        raffles.MapPost("", async (Loyalty.Core.Modules.Raffles.Application.CreateRaffleInput req, Loyalty.Core.Modules.Raffles.Application.RaffleService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateAsync(req, ct), statusCode: 201));
        raffles.MapGet("", async (Loyalty.Core.Modules.Raffles.Application.RaffleService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        raffles.MapGet("/{id:guid}/entries", async (Guid id, Loyalty.Core.Modules.Raffles.Application.RaffleService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.EntriesAsync(id, ct) }));
        raffles.MapPost("/{id:guid}/draw", async (Guid id, Loyalty.Core.Modules.Raffles.Application.RaffleService svc, CancellationToken ct) =>
            await svc.DrawAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        raffles.MapDelete("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Raffles.Application.RaffleService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // ---------- customer-facing app (P11): member-token auth, own data only ----------
        var customer = v1.MapGroup("/customer");
        customer.MapGet("/me", async (Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
        {
            var m = await svc.GetMemberAsync(ct);
            if (m is null) return Results.NotFound();
            return Results.Ok(new { member = m, profile = await svc.GetProfileAsync(ct) });
        });
        customer.MapGet("/transactions", async (Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            await svc.GetTransactionsAsync(ct) is { } v ? Results.Ok(new { items = v }) : Results.NotFound());
        customer.MapGet("/rewards", async (Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRewardsAsync(ct)));
        customer.MapGet("/redemptions", async (Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.GetRedemptionsAsync(ct) }));
        customer.MapPost("/redemptions", async (RedeemRequest req, Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            Results.Json(await svc.ReserveAsync(req.RewardId, ct), statusCode: 201));
        customer.MapPost("/redemptions/{id:guid}/cancel", async (Guid id, Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            await svc.CancelAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        customer.MapGet("/stamp-cards", async (Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.GetStampCardsAsync(ct) }));
        customer.MapGet("/achievements", async (Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            await svc.GetAchievementsAsync(ct) is { } v ? Results.Ok(new { items = v }) : Results.NotFound());
        customer.MapGet("/referrals", async (Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.GetReferralsAsync(ct) }));
        customer.MapGet("/raffles", async (Loyalty.Core.Modules.Customers.Application.CustomerService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.GetRafflesAsync(ct) }));

        // ---------- events + request status ----------
        v1.MapPost("/events", async (LoyaltyEventEnvelopeV1 envelope, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            EventIngestionService svc, CancellationToken ct) =>
        {
            var canonical = JsonSerializer.Serialize(envelope, JsonOptionsProvider.Default);
            var result = await svc.SubmitAsync(envelope, canonical, idempotencyKey, ct);
            return Results.Json(result.Body, statusCode: result.StatusCode);
        }).RequireRateLimiting("events");
        v1.MapGet("/requests/{id:guid}", async (Guid id, EventIngestionService svc, CancellationToken ct) =>
            await svc.GetRequestStatusAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        v1.MapGet("/requests", async (string? status, int? limit, EventIngestionService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.ListRequestsAsync(status, limit ?? 50, ct) }));
        v1.MapPost("/requests/{id:guid}/replay", async (Guid id, EventIngestionService svc, CancellationToken ct) =>
            await svc.ReplayAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        // ---------- admin: trigger the lifecycle inactivity sweep on demand ----------
        v1.MapPost("/admin/lifecycle-sweep", async (Loyalty.Core.Modules.Cvm.Application.LifecycleSweepService svc, CancellationToken ct) =>
            Results.Ok(new { transitions = await svc.RunAsync(ct) }));

        // ---------- points expiry (P13): recompute on demand ----------
        v1.MapPost("/admin/expire-points", async (string? asOf, Loyalty.Core.Modules.Wallets.Application.PointsExpiryService svc, CancellationToken ct) =>
            Results.Ok(new { expired = asOf is not null && DateTimeOffset.TryParse(asOf, out var d) ? await svc.RunForAsync(d, ct) : await svc.RunAsync(ct) }));

        // ---------- RFM personas (P12): recompute + distribution ----------
        v1.MapPost("/admin/rfm-recompute", async (Loyalty.Core.Modules.Cvm.Application.RfmService svc, CancellationToken ct) =>
            Results.Ok(new { scored = await svc.RunAsync(ct) }));
        v1.MapGet("/analytics/rfm", async (Loyalty.Core.Modules.Cvm.Application.RfmService svc, CancellationToken ct) =>
            Results.Ok(await svc.SummaryAsync(ct)));

        // ---------- admin: trigger the daily birthday reward sweep on demand (P10) ----------
        v1.MapPost("/admin/birthday-sweep", async (string? asOf, Loyalty.Core.Modules.Engagement.Application.BirthdaySweepService svc, CancellationToken ct) =>
            Results.Ok(new { rewarded = asOf is not null && DateTimeOffset.TryParse(asOf, out var d) ? await svc.RunForAsync(d, ct) : await svc.RunAsync(ct) }));

        // ---------- analytics ----------
        v1.MapGet("/analytics/promotions", async (Loyalty.Core.Modules.Analytics.PromotionAnalyticsService svc, CancellationToken ct) =>
            Results.Ok(await svc.SummaryAsync(ct)));
        v1.MapGet("/analytics/overview", async (Loyalty.Core.Modules.Analytics.AnalyticsService svc, CancellationToken ct) =>
            Results.Ok(await svc.OverviewAsync(ct)));

        // ---------- notifications (result-event feed) ----------
        v1.MapGet("/notifications", async (int? limit, Loyalty.Core.Modules.Notifications.Application.NotificationService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.ListEventsAsync(limit ?? 50, ct) }));

        // ---------- webhooks ----------
        var webhooks = v1.MapGroup("/webhooks");
        webhooks.MapPost("", async (Loyalty.Core.Modules.Notifications.Application.CreateWebhookInput req, Loyalty.Core.Modules.Notifications.Application.NotificationService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateWebhookAsync(req, ct), statusCode: 201));
        webhooks.MapGet("", async (Loyalty.Core.Modules.Notifications.Application.NotificationService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListWebhooksAsync(ct)));
        webhooks.MapPatch("/{id:guid}/status", async (Guid id, SetStatusRequest req, Loyalty.Core.Modules.Notifications.Application.NotificationService svc, CancellationToken ct) =>
            await svc.SetWebhookActiveAsync(id, req.IsActive, ct) ? Results.NoContent() : Results.NotFound());
        webhooks.MapDelete("/{id:guid}", async (Guid id, Loyalty.Core.Modules.Notifications.Application.NotificationService svc, CancellationToken ct) =>
            await svc.DeleteWebhookAsync(id, ct) ? Results.NoContent() : Results.NotFound());
        // on-demand delivery (ops/testing)
        webhooks.MapPost("/dispatch", async (Loyalty.Core.Modules.Notifications.Application.WebhookDispatcher d, CancellationToken ct) =>
            Results.Ok(new { dispatched = await d.DispatchPendingAsync(ct) }));
    }
}
