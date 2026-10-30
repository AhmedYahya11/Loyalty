namespace Loyalty.Core.Modules.Campaigns.Domain;

/// <summary>Controlled operator registry for campaign and segment conditions (MVP spec §12).</summary>
public enum ConditionOperator
{
    Equals = 0,
    NotEquals = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5,
    In = 6,
    DateRange = 7,
}

/// <summary>A single condition over an event/profile fact, e.g. attributes.channel Equals "mobile".</summary>
public sealed record CampaignCondition(string Field, ConditionOperator Operator, string Value);

/// <summary>MVP effect types (§12). AwardBasePoints computes from amount via loyalty settings.</summary>
public enum EffectType
{
    AwardPoints = 0,
    AwardBasePoints = 1,
    ReversePoints = 2,
    AdjustPoints = 3,
    NoReward = 4,
    /// <summary>Grant a specific catalog reward/coupon to the member at no point cost (P16).</summary>
    GrantReward = 5,
}

/// <summary>
/// A typed effect specification stored on the campaign. Points is used by AwardPoints /
/// ReversePoints / AdjustPoints; Multiplier scales base points for AwardBasePoints; RewardId is the
/// catalog reward granted by GrantReward.
/// </summary>
public sealed record CampaignEffect(
    string EffectId,
    EffectType Type,
    long? Points = null,
    decimal? Multiplier = null,
    Guid? RewardId = null);

public enum StackingPolicy { Exclusive = 0, Stackable = 1 }

/// <summary>The window a member limit resets over (MVP spec §12).</summary>
public enum LimitPeriod { AllTime = 0, CalendarDay = 1, CalendarMonth = 2 }

/// <summary>Max executions of a campaign for one member within a period.</summary>
public sealed record MemberLimit(LimitPeriod Period, int MaximumExecutions)
{
    /// <summary>Deterministic usage key for the period from the event's OccurredAt.</summary>
    public string PeriodKey(DateTimeOffset occurredAt) => Period switch
    {
        LimitPeriod.CalendarDay => occurredAt.UtcDateTime.ToString("yyyy-MM-dd"),
        LimitPeriod.CalendarMonth => occurredAt.UtcDateTime.ToString("yyyy-MM"),
        _ => "all",
    };
}

public enum CampaignStatus
{
    Draft = 0,
    Validated = 1,
    Active = 2,
    Paused = 3,
    Completed = 4,
    Archived = 5,
}
