namespace AllWorkHRIS.Module.Payroll.Domain.Calendar;

public sealed record PayrollContext
{
    public Guid         PayrollContextId           { get; init; }
    public string       PayrollContextCode         { get; init; } = default!;
    public string       PayrollContextName         { get; init; } = default!;
    public Guid         LegalEntityId              { get; init; }
    public int          PayFrequencyId             { get; init; }
    public string       ContextStatus              { get; init; } = default!;
    public Guid?        ParentPayrollContextId     { get; init; }
    public Guid?        RootPayrollContextId       { get; init; }
    public int          ContextVersionNumber       { get; init; }
    public string?      ContextChangeReasonCode    { get; init; }
    public DateOnly     EffectiveStartDate         { get; init; }
    public DateOnly?    EffectiveEndDate           { get; init; }
    public Guid         CreatedBy                  { get; init; }
    public DateTimeOffset CreationTimestamp        { get; init; }
    public Guid         LastUpdatedBy              { get; init; }
    public DateTimeOffset LastUpdateTimestamp      { get; init; }
}
