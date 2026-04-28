// AllWorkHRIS.Core/Events/RehireEventPayload.cs
namespace AllWorkHRIS.Core.Events;

public sealed class RehireEventPayload
{
    public required Guid     EmploymentId     { get; init; }
    public required Guid     PersonId         { get; init; }
    public required Guid     EventId          { get; init; }
    public required Guid     TenantId         { get; init; }
    public required DateOnly EffectiveDate    { get; init; }
    public Guid?             PriorEmploymentId { get; init; }
    public Guid?             PayrollContextId  { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
