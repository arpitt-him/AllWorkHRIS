namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record LeaveRequest
{
    public Guid     LeaveRequestId      { get; init; }
    public Guid     EmploymentId        { get; init; }
    public int      LeaveTypeId         { get; init; }
    public DateOnly RequestDate         { get; init; }
    public DateOnly LeaveStartDate      { get; init; }
    public DateOnly LeaveEndDate        { get; init; }
    public DateOnly? ActualReturnDate   { get; init; }
    public int      LeaveStatusId       { get; init; }
    public string   LeaveReasonCode     { get; init; } = default!;
    public int      PayrollImpactTypeId { get; init; }
    public decimal? LeaveBalanceImpact  { get; init; }
    public Guid?    ApprovedBy          { get; init; }
    public DateTimeOffset? ApprovalTimestamp { get; init; }
    public Guid?    HrContactId         { get; init; }
    public bool     FmlaEligibleFlag    { get; init; }
    public string?  Notes               { get; init; }
    public Guid     CreatedBy           { get; init; }
    public DateTimeOffset CreationTimestamp  { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
    public Guid     LastUpdatedBy       { get; init; }
}

public sealed record LeaveBalance
{
    public Guid     LeaveBalanceId      { get; init; }
    public Guid     EmploymentId        { get; init; }
    public int      LeaveTypeId         { get; init; }
    public decimal  AvailableBalance    { get; init; }
    public decimal  PendingBalance      { get; init; }
    public decimal  UsedBalance         { get; init; }
    public decimal  EntitlementTotal    { get; init; }
    public DateOnly PlanYearStart       { get; init; }
    public DateOnly PlanYearEnd         { get; init; }
    public DateOnly? LastAccrualDate    { get; init; }
    public Guid?    LastUpdatedRunId    { get; init; }
    public DateTimeOffset CreatedTimestamp    { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
}

public sealed record LeaveTypeInfo(
    int    Id,
    string Code,
    bool   IsAccrued,
    string PayrollImpactCode,
    decimal? PayPercentage);
