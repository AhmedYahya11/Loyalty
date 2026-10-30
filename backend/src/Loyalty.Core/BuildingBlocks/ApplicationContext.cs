namespace Loyalty.Core.BuildingBlocks;

/// <summary>
/// Mutable, request/handler-scoped implementation of <see cref="IApplicationContext"/>.
/// Registered as scoped; established exactly once per request (API-key middleware) or
/// per durable message (worker), then read by the DbContext query filter.
/// </summary>
public sealed class ApplicationContext : IApplicationContext
{
    private Guid[] _visible = [];

    public Guid ApplicationId { get; private set; }
    public Guid AccountId { get; private set; }
    public string SourceSystem { get; private set; } = string.Empty;
    public bool IsEstablished { get; private set; }
    public bool IsSystem { get; private set; }
    public IReadOnlyCollection<Guid> VisibleApplicationIds => _visible;

    /// <summary>Enter the narrow system scope (auth lookup, closure resolution, bootstrap).</summary>
    public void EnterSystemScope() => IsSystem = true;

    public void LeaveSystemScope() => IsSystem = false;

    /// <summary>Establish the business scope after authentication.</summary>
    public void Establish(Guid applicationId, Guid accountId, string sourceSystem, IEnumerable<Guid> visibleApplicationIds)
    {
        ApplicationId = applicationId;
        AccountId = accountId;
        SourceSystem = sourceSystem;
        _visible = visibleApplicationIds.Distinct().ToArray();
        IsEstablished = true;
    }
}
