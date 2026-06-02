namespace AllWorkHRIS.Module.TimeAttendance.Commands;

public sealed record SubmitTimeEntryCommand
{
    public required Guid    EmploymentId    { get; init; }
    public required DateOnly WorkDate       { get; init; }
    public required string  TimeCategory    { get; init; }
    public required decimal Duration        { get; init; }
    public TimeOnly?        StartTime       { get; init; }
    public TimeOnly?        EndTime         { get; init; }
    public Guid?            ShiftId         { get; init; }
    public required Guid    PayrollPeriodId { get; init; }
    public required string  EntryMethod     { get; init; }
    public required Guid    SubmittedBy     { get; init; }
    public string?          Notes           { get; init; }
    public string?          ProjectCode     { get; init; }
    public string?          TaskCode        { get; init; }
    // Phase 12.7b — when true the entry lands APPROVED instead of SUBMITTED (trusted source, e.g.
    // an IMPORT into a legal entity configured for auto-approve). Set by the caller, not the user.
    public bool             AutoApprove     { get; init; }
}
