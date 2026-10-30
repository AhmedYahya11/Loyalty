namespace Loyalty.Core.BuildingBlocks;

/// <summary>
/// Request-scoped identity of the currently authenticated customer (member) for the customer-facing
/// app (P11). Established by the member-token auth branch alongside the <see cref="ApplicationContext"/>.
/// Customer endpoints read <see cref="MemberId"/> to constrain every query/action to the caller's own
/// data. Unset for admin (API-key) requests.
/// </summary>
public sealed class CustomerContext
{
    public Guid ApplicationId { get; private set; }
    public Guid MemberId { get; private set; }
    public bool IsEstablished { get; private set; }

    public void Establish(Guid applicationId, Guid memberId)
    {
        ApplicationId = applicationId;
        MemberId = memberId;
        IsEstablished = true;
    }
}
