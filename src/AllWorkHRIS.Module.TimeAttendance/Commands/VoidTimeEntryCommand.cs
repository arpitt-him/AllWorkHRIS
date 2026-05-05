namespace AllWorkHRIS.Module.TimeAttendance.Commands;

public sealed record VoidTimeEntryCommand
{
    public required Guid   TimeEntryId { get; init; }
    public required Guid   VoidedBy    { get; init; }
    public required string Reason      { get; init; }
}
