namespace Loyalty.Core.Modules.Applications.Domain;

/// <summary>
/// Closure table for the application hierarchy. Each application has a self-row with
/// depth 0; creation writes all ancestor rows transactionally. Visibility (self +
/// descendants) is computed by joining this table (MVP spec §4, §7).
/// Technical metadata — unscoped.
/// </summary>
public sealed class ApplicationClosure
{
    public Guid AncestorApplicationId { get; private set; }
    public Guid DescendantApplicationId { get; private set; }
    public int Depth { get; private set; }

    private ApplicationClosure() { }

    public ApplicationClosure(Guid ancestor, Guid descendant, int depth)
    {
        AncestorApplicationId = ancestor;
        DescendantApplicationId = descendant;
        Depth = depth;
    }
}
