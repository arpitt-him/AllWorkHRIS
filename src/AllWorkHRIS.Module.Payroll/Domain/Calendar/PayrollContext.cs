namespace AllWorkHRIS.Module.Payroll.Domain.Calendar;

public sealed record PayrollContext
{
    public Guid         PayrollContextId           { get; init; }
    public string       PayrollContextCode         { get; init; } = default!;
    public string       PayrollContextName         { get; init; } = default!;
    public Guid         LegalEntityId              { get; init; }
    public int          PayFrequencyId             { get; init; }
    public int?         CompensationRateTypeId     { get; init; }
    public string       ContextStatus              { get; init; } = default!;
    public Guid?        ParentPayrollContextId     { get; init; }
    public Guid?        RootPayrollContextId       { get; init; }
    public int          ContextVersionNumber       { get; init; }
    public string?      ContextChangeReasonCode    { get; init; }
    public DateOnly     EffectiveStartDate         { get; init; }
    public DateOnly?    EffectiveEndDate           { get; init; }
    public string?      PayDateConvention          { get; init; }
    public int          PayDateOffsetDays          { get; init; } = 5;
    public int          CutoffOffsetDays           { get; init; } = 3;
    public string       ExtraPeriodPolicy          { get; init; } = "EXTRA_SPECIAL";
    public decimal      OtWeeklyThresholdHours     { get; init; } = 40.00m;
    public decimal?     OtReviewThresholdHours     { get; init; }   // Phase 12.7b — null = OT never flagged on hours
    public int          WorkweekStartDay           { get; init; } = 1;
    public Guid         CreatedBy                  { get; init; }
    public DateTimeOffset CreationTimestamp        { get; init; }
    public Guid         LastUpdatedBy              { get; init; }
    public DateTimeOffset LastUpdateTimestamp      { get; init; }
}

// ADR-024 / Phase 12.13.3: one effective-dated OT-config interval for a payroll context — the
// history/scheduled rows shown on the pay-calendar detail page and read by the shared resolver.
public sealed record DatedOtConfigRow
{
    public DateOnly  EffectiveDate          { get; init; }
    public DateOnly? EndDate                { get; init; }
    public decimal   OtWeeklyThresholdHours { get; init; }
    public int       WorkweekStartDay       { get; init; }
    public decimal?  OtReviewThresholdHours { get; init; }
}
