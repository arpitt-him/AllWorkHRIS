// AllWorkHRIS.Core/Events/CompensationChangeEventPayload.cs
namespace AllWorkHRIS.Core.Events;

public sealed class CompensationChangeEventPayload
{
    public required Guid     EmploymentId  { get; init; }
    public required Guid     PersonId      { get; init; }
    public required Guid     EventId       { get; init; }
    public required Guid     TenantId      { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required string   RateType      { get; init; }
    public required decimal  NewBaseRate   { get; init; }
    public required string   PayFrequency  { get; init; }
    public required bool     IsRetroactive { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
