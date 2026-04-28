// AllWorkHRIS.Core/Events/HireEventPayload.cs
namespace AllWorkHRIS.Core.Events;

public sealed class HireEventPayload
{
    public required Guid     EmploymentId     { get; init; }
    public required Guid     PersonId         { get; init; }
    public required Guid     EventId          { get; init; }
    public required Guid     TenantId         { get; init; }
    public required DateOnly EffectiveDate    { get; init; }
    public required Guid     LegalEntityId    { get; init; }
    public required string   FlsaStatus       { get; init; }
    public Guid?             PayrollContextId { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
