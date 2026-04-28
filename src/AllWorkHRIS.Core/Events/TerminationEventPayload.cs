// AllWorkHRIS.Core/Events/TerminationEventPayload.cs
namespace AllWorkHRIS.Core.Events;

public sealed class TerminationEventPayload
{
    public required Guid EmploymentId { get; init; }
    public required Guid PersonId { get; init; }
    public required Guid TenantId { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required string TerminationReason { get; init; }
    public required bool IsVoluntary { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
