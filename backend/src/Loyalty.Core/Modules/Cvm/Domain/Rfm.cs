namespace Loyalty.Core.Modules.Cvm.Domain;

/// <summary>
/// RFM (Recency, Frequency, Monetary) scoring + persona classification (P12). Scores are relative
/// quintiles (1–5) within an application's member population; the persona is the standard marketing
/// matrix derived from Recency and the combined Frequency/Monetary score. Persona keys are stable
/// English identifiers; the UI localizes them (EN/AR/KU).
/// </summary>
public static class Rfm
{
    public static readonly string[] Personas =
    [
        "Champions", "LoyalCustomers", "PotentialLoyalist", "NewCustomers", "Promising",
        "NeedAttention", "AtRisk", "Hibernating", "Lost",
    ];

    /// <summary>
    /// Quintile score (1–5) for <paramref name="value"/> against the ascending-sorted population:
    /// the higher the value, the higher the score. Uses the percentile of values ≤ this one.
    /// </summary>
    public static int Quintile(double value, IReadOnlyList<double> ascending)
    {
        if (ascending.Count == 0) return 3;
        var countLe = 0;
        foreach (var v in ascending) if (v <= value) countLe++;
        var pct = (double)countLe / ascending.Count; // (0, 1]
        var score = (int)Math.Ceiling(pct * 5);
        return Math.Clamp(score, 1, 5);
    }

    /// <summary>Classify a member into a persona from Recency and combined Frequency/Monetary scores.</summary>
    public static string Classify(int recency, int frequency, int monetary)
    {
        var fm = (int)Math.Round((frequency + monetary) / 2.0, MidpointRounding.AwayFromZero);
        return (recency, fm) switch
        {
            ( >= 4, >= 4) => "Champions",
            ( >= 3, >= 3) => "LoyalCustomers",
            ( >= 4, _) => fm <= 1 ? "NewCustomers" : "PotentialLoyalist",
            (3, _) => "Promising",
            ( <= 2, >= 3) => "AtRisk",
            (2, _) => "NeedAttention",
            ( <= 1, >= 2) => "Hibernating",
            _ => "Lost",
        };
    }
}
