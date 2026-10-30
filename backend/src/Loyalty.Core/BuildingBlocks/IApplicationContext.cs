namespace Loyalty.Core.BuildingBlocks;

/// <summary>
/// The ambient application scope for the current request or durable-message handler.
/// Both the API (from the authenticated API key) and the Worker (from durable-message
/// headers) converge on this single abstraction so a query behaves identically in both
/// (MVP spec §7, PROCESSING_FLOW diagram 2).
/// </summary>
public interface IApplicationContext
{
    /// <summary>The application that owns the data being read or written.</summary>
    Guid ApplicationId { get; }

    /// <summary>The authenticated API-credential account.</summary>
    Guid AccountId { get; }

    /// <summary>
    /// The source system for emitted data, derived from the authenticated account (its name).
    /// Never trusted from the request body (MVP spec §8).
    /// </summary>
    string SourceSystem { get; }

    /// <summary>True once an application scope has been established.</summary>
    bool IsEstablished { get; }

    /// <summary>
    /// True only inside the narrow system scope used for authentication lookups,
    /// closure resolution, and bootstrap. The EF global query filter is bypassed here.
    /// </summary>
    bool IsSystem { get; }

    /// <summary>
    /// The authenticated application plus all of its descendants (resolved from
    /// ApplicationClosure). Visibility is "self + descendants"; a child can never
    /// see a parent or sibling. Backs the EF global query filter.
    /// </summary>
    IReadOnlyCollection<Guid> VisibleApplicationIds { get; }
}
