// AllWorkHRIS.Core/Events/HireEventPayload.cs
namespace AllWorkHRIS.Core.Events;

public sealed class HireEventPayload
{
    public required Guid EmploymentId { get; init; }
    public required Guid PersonId { get; init; }
    public required Guid TenantId { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required string EmployeeNumber { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public Guid? PayrollContextId { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
