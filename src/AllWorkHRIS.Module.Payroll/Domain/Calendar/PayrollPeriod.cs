namespace AllWorkHRIS.Module.Payroll.Domain.Calendar;

public sealed record PayrollPeriod
{
    public Guid         PeriodId                      { get; init; }
    public Guid         PayrollContextId              { get; init; }
    public int          PeriodYear                    { get; init; }
    public int          PeriodNumber                  { get; init; }
    public DateOnly     PeriodStartDate               { get; init; }
    public DateOnly     PeriodEndDate                 { get; init; }
    public DateOnly     PayDate                       { get; init; }
    public DateOnly     InputCutoffDate               { get; init; }
    public DateOnly?    CalculationDate               { get; init; }
    public DateOnly?    CorrectionWindowCloseDate     { get; init; }
    public DateOnly?    FinalizationDate              { get; init; }
    public DateOnly?    TransmissionDate              { get; init; }
    public string       CalendarStatus                { get; init; } = default!;
    public Guid?        ParentCalendarEntryId         { get; init; }
    public Guid?        RootCalendarEntryId           { get; init; }
    public int          CalendarVersionNumber         { get; init; }
    public string?      CalendarChangeReasonCode      { get; init; }
    public Guid         CreatedBy                     { get; init; }
    public DateTimeOffset CreationTimestamp           { get; init; }
    public Guid         LastUpdatedBy                 { get; init; }
    public DateTimeOffset LastUpdateTimestamp         { get; init; }
}
