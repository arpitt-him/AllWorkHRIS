namespace AllWorkHRIS.Module.TimeAttendance.Domain.Schedule;

public sealed record ShiftDefinition
{
    public Guid            ShiftId          { get; init; }
    public Guid            WorkScheduleId   { get; init; }
    public string          Name             { get; init; } = string.Empty;
    public int             DayOfWeek        { get; init; }
    public TimeOnly        StartTime        { get; init; }
    public TimeOnly        EndTime          { get; init; }
    public decimal         DurationHours    { get; init; }
}
