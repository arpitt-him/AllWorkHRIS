namespace AllWorkHRIS.Module.TimeAttendance.Domain.Schedule;

public sealed record WorkSchedule
{
    public Guid            WorkScheduleId         { get; init; }
    public Guid            LegalEntityId          { get; init; }
    public string          Name                   { get; init; } = string.Empty;
    public decimal         StandardHoursPerWeek   { get; init; }
    public int             WorkweekStartDay        { get; init; }
    public DateOnly        EffectiveDate          { get; init; }
    public DateOnly?       EndDate                { get; init; }
    public DateTimeOffset  CreatedAt              { get; init; }
    public DateTimeOffset  UpdatedAt              { get; init; }
}
