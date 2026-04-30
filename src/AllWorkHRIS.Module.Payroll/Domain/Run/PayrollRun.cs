namespace AllWorkHRIS.Module.Payroll.Domain.Run;

public sealed record PayrollRun
{
    public Guid        RunId                      { get; init; }
    public Guid        PayrollContextId           { get; init; }
    public Guid        PeriodId                   { get; init; }
    public DateOnly    PayDate                    { get; init; }
    public int         RunTypeId                  { get; init; }
    public int         RunStatusId                { get; init; }
    public string?     RunDescription             { get; init; }
    public Guid?       ParentRunId                { get; init; }
    public Guid?       RelatedRunGroupId          { get; init; }
    public string?     RuleAndConfigVersionRef    { get; init; }
    public bool        TemporalOverrideActiveFlag { get; init; }
    public DateOnly?   TemporalOverrideDate       { get; init; }
    public Guid        InitiatedBy                { get; init; }
    public DateTimeOffset? RunStartTimestamp      { get; init; }
    public DateTimeOffset? RunEndTimestamp        { get; init; }
    public Guid        CreatedBy                  { get; init; }
    public DateTimeOffset CreationTimestamp       { get; init; }
    public Guid        LastUpdatedBy              { get; init; }
    public DateTimeOffset LastUpdateTimestamp     { get; init; }
}
