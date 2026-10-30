namespace Loyalty.Core.Modules.Settings.Domain;

/// <summary>Deterministic base-point calculation shared by live settings and frozen snapshots.</summary>
public static class PointsMath
{
    /// <summary>floor(amount / spendAmountPerPoint) under the Floor rounding policy (§10).</summary>
    public static long CalculateBasePoints(decimal amount, decimal spendAmountPerPoint, RoundingPolicy rounding)
    {
        if (spendAmountPerPoint <= 0) return 0;
        var points = amount / spendAmountPerPoint;
        return rounding switch
        {
            RoundingPolicy.Floor => (long)Math.Floor(points),
            _ => (long)Math.Floor(points),
        };
    }
}
