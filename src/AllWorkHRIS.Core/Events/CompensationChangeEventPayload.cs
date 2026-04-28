// AllWorkHRIS.Core/Events/CompensationChangeEventPayload.cs
namespace AllWorkHRIS.Core.Events;

public sealed class CompensationChangeEventPayload
{
    public required Guid EmploymentId { get; init; }
    public required Guid PersonId { get; init; }
    public required Guid TenantId { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required decimal NewRate { get; init; }
    public required string RateType { get; init; }   // e.g. ANNUAL, HOURLY
    public required string CurrencyCode { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
