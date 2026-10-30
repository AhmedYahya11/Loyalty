using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Gamification.Domain;

/// <summary>Member metrics gamification rules measure against.</summary>
public enum GamificationMetric { PurchaseCount = 0, NetSpend = 1, PointsEarned = 2, LoginCount = 3, ReferralCount = 4, Streak = 5 }

/// <summary>A snapshot of the member metrics used to evaluate achievements/challenges.</summary>
public readonly record struct GamificationMetrics(long PurchaseCount, decimal NetSpend, long PointsEarned, long LoginCount, long ReferralCount, int Streak)
{
    public decimal Value(GamificationMetric m) => m switch
    {
        GamificationMetric.PurchaseCount => PurchaseCount,
        GamificationMetric.NetSpend => NetSpend,
        GamificationMetric.PointsEarned => PointsEarned,
        GamificationMetric.LoginCount => LoginCount,
        GamificationMetric.ReferralCount => ReferralCount,
        GamificationMetric.Streak => Streak,
        _ => 0,
    };
}

/// <summary>A one-time unlockable badge awarded when a member metric reaches a threshold.</summary>
public sealed class Achievement : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? NameAr { get; private set; }
    public string? Description { get; private set; }
    public string? Icon { get; private set; }
    public GamificationMetric Metric { get; private set; }
    public decimal Threshold { get; private set; }
    public long BonusPoints { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Achievement() { }

    public static Achievement Create(Guid applicationId, string name, string? nameAr, string? description, string? icon,
        GamificationMetric metric, decimal threshold, long bonusPoints, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new LoyaltyException(LoyaltyError.ValidationFailed("name is required."));
        if (threshold <= 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("threshold must be > 0."));
        if (bonusPoints < 0) throw new LoyaltyException(LoyaltyError.ValidationFailed("bonusPoints must be >= 0."));
        return new Achievement
        {
            Id = Guid.NewGuid(), ApplicationId = applicationId, Name = name.Trim(),
            NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(), Description = description, Icon = icon,
            Metric = metric, Threshold = threshold, BonusPoints = bonusPoints, IsActive = true, CreatedAt = now, UpdatedAt = now,
        };
    }

    public void Update(string name, string? nameAr, string? description, string? icon, decimal threshold, long bonusPoints, bool isActive, DateTimeOffset now)
    {
        Name = name.Trim(); NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim(); Description = description; Icon = icon;
        Threshold = threshold; BonusPoints = bonusPoints; IsActive = isActive; UpdatedAt = now;
    }

    public bool Unlocked(GamificationMetrics m) => m.Value(Metric) >= Threshold;
}

/// <summary>A member's unlocked achievement. UNIQUE(app, member, achievement).</summary>
public sealed class MemberAchievement : IApplicationScoped
{
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid AchievementId { get; private set; }
    public DateTimeOffset UnlockedAt { get; private set; }

    private MemberAchievement() { }

    public MemberAchievement(Guid applicationId, Guid memberId, Guid achievementId, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); ApplicationId = applicationId; MemberId = memberId; AchievementId = achievementId; UnlockedAt = now;
    }
}
