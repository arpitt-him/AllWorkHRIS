namespace AllWorkHRIS.Core.Events;

public sealed class LeaveApprovedPayload
{
    public required Guid     LeaveRequestId    { get; init; }
    public required Guid     EmploymentId      { get; init; }
    public required Guid     TenantId          { get; init; }
    public required string   LeaveType         { get; init; }
    public required DateOnly LeaveStartDate    { get; init; }
    public required DateOnly LeaveEndDate      { get; init; }
    public required string   PayrollImpactType { get; init; }
    public required Guid     ApprovedBy        { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
}
