namespace AllWorkHRIS.Module.TimeAttendance.Commands;

public sealed record CorrectTimeEntryCommand
{
    public required Guid    OriginalTimeEntryId { get; init; }
    public required DateOnly WorkDate           { get; init; }
    public required string  TimeCategory        { get; init; }
    public required decimal Duration            { get; init; }
    public TimeOnly?        StartTime           { get; init; }
    public TimeOnly?        EndTime             { get; init; }
    public required string  CorrectionReason    { get; init; }
    public required Guid    CorrectedBy         { get; init; }
    public bool             RetroactiveFlag     { get; init; }
}
