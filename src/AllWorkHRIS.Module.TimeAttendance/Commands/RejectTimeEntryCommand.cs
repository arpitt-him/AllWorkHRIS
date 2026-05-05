namespace AllWorkHRIS.Module.TimeAttendance.Commands;

public sealed record RejectTimeEntryCommand
{
    public required Guid   TimeEntryId { get; init; }
    public required Guid   RejectedBy  { get; init; }
    public required string Reason      { get; init; }
}
