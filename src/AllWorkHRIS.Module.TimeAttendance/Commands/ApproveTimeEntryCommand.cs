namespace AllWorkHRIS.Module.TimeAttendance.Commands;

public sealed record ApproveTimeEntryCommand
{
    public required Guid TimeEntryId { get; init; }
    public required Guid ApprovedBy  { get; init; }
}
