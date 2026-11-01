using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Engagement.Domain;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Engagement.Application;

public sealed record EngagementSettingsInput(bool ReferralEnabled, long ReferrerPoints, long RefereePoints, bool BirthdayEnabled, long BirthdayBonusPoints);
public sealed record EngagementSettingsView(bool ReferralEnabled, long ReferrerPoints, long RefereePoints, bool BirthdayEnabled, long BirthdayBonusPoints);

/// <summary>Upsert/read the per-application engagement configuration (referral program + birthday reward).</summary>
public sealed class EngagementSettingsService(IRepository<Loyalty.Core.Modules.Engagement.Domain.EngagementSettings> engagementSettingsRepo, IUnitOfWork uow, ApplicationContext scope, IClock clock)
{
    public async Task<EngagementSettingsView> UpsertAsync(Guid appId, EngagementSettingsInput i, CancellationToken ct)
    {
        if (appId != scope.ApplicationId)
            throw new LoyaltyException(LoyaltyError.ValidationFailed("appId must match the authenticated application."));
        var s = await engagementSettingsRepo.Query().FirstOrDefaultAsync(x => x.ApplicationId == appId, ct);
        if (s is null) { s = EngagementSettings.Create(appId, clock.UtcNow); engagementSettingsRepo.Add(s); }
        s.Update(i.ReferralEnabled, i.ReferrerPoints, i.RefereePoints, i.BirthdayEnabled, i.BirthdayBonusPoints, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return ToView(s);
    }

    public async Task<EngagementSettingsView> GetAsync(Guid appId, CancellationToken ct)
    {
        var s = await engagementSettingsRepo.Query().FirstOrDefaultAsync(x => x.ApplicationId == appId, ct);
        return s is null ? new EngagementSettingsView(false, 0, 0, false, 0) : ToView(s);
    }

    private static EngagementSettingsView ToView(EngagementSettings s) =>
        new(s.ReferralEnabled, s.ReferrerPoints, s.RefereePoints, s.BirthdayEnabled, s.BirthdayBonusPoints);
}
