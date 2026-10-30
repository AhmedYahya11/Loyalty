namespace Loyalty.Core.Modules.Cvm.Domain;

public enum CvmStatus { Draft = 0, Active = 1, Paused = 2, Closed = 3 }

/// <summary>MVP lifecycle stages (MVP spec §11.3).</summary>
public enum LifecycleStage { New = 0, Active = 1, AtRisk = 2, Dormant = 3, Churned = 4 }
