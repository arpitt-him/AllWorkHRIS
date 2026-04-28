// AllWorkHRIS.Core/Events/TerminationEventPayload.cs
namespace AllWorkHRIS.Core.Events;

public sealed class TerminationEventPayload
{
    public required Guid     EmploymentId     { get; init; }
    public required Guid     PersonId         { get; init; }
    public required Guid     EventId          { get; init; }
    public required Guid     TenantId         { get; init; }
    public required DateOnly TerminationDate  { get; init; }
    public required string   EventType        { get; init; }
    public required string   ReasonCode       { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
