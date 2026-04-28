// AllWorkHRIS.Core/Events/ReturnToWorkPayload.cs
namespace AllWorkHRIS.Core.Events;

public sealed class ReturnToWorkPayload
{
    public required Guid LeaveRequestId { get; init; }
    public required Guid EmploymentId { get; init; }
    public required Guid TenantId { get; init; }
    public required DateOnly ReturnDate { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
