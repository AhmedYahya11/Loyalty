namespace Loyalty.Core.Modules.Cvm.Domain;

/// <summary>
/// Builds the controlled fact set a segment condition may reference (MVP spec §11.4).
/// Date comparisons receive the captured evaluation timestamp explicitly; expressions never
/// call the system clock.
/// </summary>
public static class ProfileFacts
{
    public static IReadOnlyDictionary<string, object?> Of(MemberValueProfile p) =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["lifecycleStage"] = p.LifecycleStage.ToString(),
            ["purchaseCount"] = p.PurchaseCount,
            ["netSpendAmount"] = p.NetSpendAmount,
            ["loginCount"] = p.LoginCount,
            ["referralCount"] = p.ReferralCount,
            ["pointsEarned"] = p.PointsEarned,
            ["pointsRedeemed"] = p.PointsRedeemed,
            ["pointsExpired"] = p.PointsExpired,
            ["firstActivityAt"] = p.FirstActivityAt,
            ["lastActivityAt"] = p.LastActivityAt,
            ["lastPurchaseAt"] = p.LastPurchaseAt,
        };

    /// <summary>Allowed condition fields for segment validation.</summary>
    public static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "lifecycleStage", "purchaseCount", "netSpendAmount", "loginCount", "referralCount",
        "pointsEarned", "pointsRedeemed", "pointsExpired", "firstActivityAt", "lastActivityAt", "lastPurchaseAt",
    };
}
