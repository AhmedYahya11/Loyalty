using Loyalty.Core.BuildingBlocks;

namespace Loyalty.Core.Modules.Cvm.Domain;

/// <summary>
/// Optional application-scoped Customer Value Management configuration with the same whole-row
/// nearest-ancestor fallback as loyalty settings (MVP spec §11.1). Missing/paused CVM settings
/// do not reject a valid loyalty event — they only disable lifecycle/segment evaluation.
/// </summary>
public sealed class ApplicationCvmSettings : IApplicationScoped
{
    public Guid ApplicationId { get; private set; }
    public int Version { get; private set; }
    public CvmStatus CvmStatus { get; private set; }
    public int NewPeriodDays { get; private set; }
    public int AtRiskAfterDays { get; private set; }
    public int DormantAfterDays { get; private set; }
    public int ChurnedAfterDays { get; private set; }
    public decimal HighValueNetSpendThreshold { get; private set; }
    public long FrequentPurchaseThreshold { get; private set; }
    public int EvaluationWindowDays { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ApplicationCvmSettings() { }

    public bool IsActive => CvmStatus == CvmStatus.Active;

    public static ApplicationCvmSettings Create(Guid applicationId, CvmFields f, DateTimeOffset now)
    {
        Validate(f);
        return new ApplicationCvmSettings
        {
            ApplicationId = applicationId,
            Version = 1,
            CvmStatus = f.CvmStatus,
            NewPeriodDays = f.NewPeriodDays,
            AtRiskAfterDays = f.AtRiskAfterDays,
            DormantAfterDays = f.DormantAfterDays,
            ChurnedAfterDays = f.ChurnedAfterDays,
            HighValueNetSpendThreshold = f.HighValueNetSpendThreshold,
            FrequentPurchaseThreshold = f.FrequentPurchaseThreshold,
            EvaluationWindowDays = f.EvaluationWindowDays,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Replace(CvmFields f, DateTimeOffset now)
    {
        Validate(f);
        Version += 1;
        CvmStatus = f.CvmStatus;
        NewPeriodDays = f.NewPeriodDays;
        AtRiskAfterDays = f.AtRiskAfterDays;
        DormantAfterDays = f.DormantAfterDays;
        ChurnedAfterDays = f.ChurnedAfterDays;
        HighValueNetSpendThreshold = f.HighValueNetSpendThreshold;
        FrequentPurchaseThreshold = f.FrequentPurchaseThreshold;
        EvaluationWindowDays = f.EvaluationWindowDays;
        UpdatedAt = now;
    }

    private static void Validate(CvmFields f)
    {
        if (f.NewPeriodDays <= 0 || f.AtRiskAfterDays <= 0 || f.DormantAfterDays <= 0 || f.ChurnedAfterDays <= 0 || f.EvaluationWindowDays <= 0)
            throw new LoyaltyException(LoyaltyError.ValidationFailed(
                "CVM day windows must be positive.", "يجب أن تكون فترات التقييم أرقامًا موجبة."));
        if (!(f.NewPeriodDays <= f.AtRiskAfterDays && f.AtRiskAfterDays < f.DormantAfterDays && f.DormantAfterDays < f.ChurnedAfterDays))
            throw new LoyaltyException(LoyaltyError.ValidationFailed(
                "Require NewPeriodDays <= AtRiskAfterDays < DormantAfterDays < ChurnedAfterDays.",
                "يجب أن يكون: فترة الجديد ≤ فترة الخطر < فترة الخمول < فترة الفقدان."));
    }
}

/// <summary>Input bundle for creating/replacing CVM settings.</summary>
public sealed record CvmFields(
    CvmStatus CvmStatus, int NewPeriodDays, int AtRiskAfterDays, int DormantAfterDays, int ChurnedAfterDays,
    decimal HighValueNetSpendThreshold, long FrequentPurchaseThreshold, int EvaluationWindowDays);
