namespace AllWorkHRIS.Core.Pipeline;

public sealed record PipelineRequest
{
    public required Guid     EmploymentId      { get; init; }
    public required Guid     EmployeeId        { get; init; }
    public required Guid     PayrollContextId  { get; init; }
    public required Guid     PeriodId          { get; init; }
    public required DateOnly PayDate           { get; init; }
    public required decimal  GrossPayPeriod    { get; init; }
    public required int      PayPeriodsPerYear { get; init; }

    // Jurisdiction resolved by the caller from employee work-location data
    public required string JurisdictionCode { get; init; }

    // Override filing status for hypothetical runs (e.g. Preview Sandbox) where no employee profile exists
    public string? FilingStatusCode { get; init; }

    // Pay period boundaries — populated by the caller from the pay calendar.
    // When not set (default DateOnly) the provider treats the period as a single day (PayDate),
    // which produces a coverage fraction of 1.0 for all elections — safe legacy behaviour.
    public DateOnly PayPeriodStart        { get; init; }
    public DateOnly PayPeriodEnd          { get; init; }

    // Calendar context for FIXED_MONTHLY computation.
    // PayDatesInPeriodMonth: how many pay dates fall in the calendar month of PayDate (2 or 3 for biweekly; 2 for semi-monthly; 4 or 5 for weekly).
    // PayDateOrdinalInMonth: which occurrence this is (1-based).
    public int      PayDatesInPeriodMonth  { get; init; } = 2;
    public int      PayDateOrdinalInMonth  { get; init; } = 1;

    // Proration policies from payroll_context — carried into the pipeline for benefit step use.
    public string   PartialPeriodRule      { get; init; } = "PRORATE_DAYS";
    public string   ThreePaycheckMonthRule { get; init; } = "PRORATE";

    // YTD accumulator balances keyed by step_code — loaded by caller from AccumulatorService
    public IReadOnlyDictionary<string, decimal> YtdBalances { get; init; }
        = new Dictionary<string, decimal>();
}
