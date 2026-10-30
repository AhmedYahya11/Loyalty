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
}

/// <summary>
/// A typed effect specification stored on the campaign. Points is used by AwardPoints /
/// ReversePoints / AdjustPoints; Multiplier scales base points for AwardBasePoints.
/// </summary>
public sealed record CampaignEffect(
    string EffectId,
    EffectType Type,
    long? Points = null,
    decimal? Multiplier = null);

public enum StackingPolicy { Exclusive = 0, Stackable = 1 }

public enum CampaignStatus
{
    Draft = 0,
    Validated = 1,
    Active = 2,
    Paused = 3,
    Completed = 4,
    Archived = 5,
}
