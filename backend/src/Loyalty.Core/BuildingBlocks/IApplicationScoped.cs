namespace Loyalty.Core.BuildingBlocks;

/// <summary>
/// Every business entity belongs to exactly one application (the tenant / security boundary).
/// The value is required and immutable after creation (MVP spec §6).
/// </summary>
public interface IApplicationScoped
{
    Guid ApplicationId { get; }
}
