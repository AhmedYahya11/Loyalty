namespace Loyalty.Core.BuildingBlocks;

/// <summary>Abstraction over the system clock so handlers and tests are deterministic.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
