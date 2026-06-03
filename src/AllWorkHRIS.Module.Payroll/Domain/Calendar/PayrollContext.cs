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

// ADR-024 / Phase 12.13.4b: a selectable time category for the OT-eligible-set editor. IsWorkedTime
// marks the default-set members (REGULAR/OVERTIME) so the UI can flag them.
public sealed record OtCategoryOption
{
    public int    Id           { get; init; }
    public string Code         { get; init; } = default!;
    public string Label        { get; init; } = default!;
    public bool   IsWorkedTime { get; init; }
}

// ADR-024 / Phase 12.13.4b: one row of the effective-dated OT-eligible set (a category within an
// interval), joined to its category code/label for the timeline. The page groups by interval.
public sealed record DatedOtEligibleRow
{
    public DateOnly  EffectiveDate { get; init; }
    public DateOnly? EndDate       { get; init; }
    public int       TimeCategoryId { get; init; }
    public string    CategoryCode  { get; init; } = default!;
    public string    CategoryLabel { get; init; } = default!;
}
