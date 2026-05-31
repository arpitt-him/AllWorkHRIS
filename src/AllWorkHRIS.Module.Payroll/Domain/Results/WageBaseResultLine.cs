namespace AllWorkHRIS.Module.Payroll.Domain.Results;

/// <summary>
/// Per-base taxable-wage snapshot the engine emits each run (ADR-019/020, Phase 12.8) —
/// e.g. Medicare wages, SS wages (= W-2 Box 5 / 3). Posts to its YTD wage-base accumulator
/// at approval via the generic impact path (resolved by <see cref="WageBaseCode"/> =
/// accumulator_definition.accumulator_code), so cap-aware steps can read exact YTD wages.
/// </summary>
public sealed record WageBaseResultLine
{
    public Guid     WageBaseResultLineId    { get; init; }
    public Guid     EmployeePayrollResultId { get; init; }
    public Guid     EmploymentId            { get; init; }
    public string   WageBaseCode            { get; init; } = default!;
    public string   WageBaseDescription     { get; init; } = default!;
    public decimal  TaxableWagesAmount      { get; init; }
    public bool     AccumulatorImpactFlag   { get; init; } = true;
    public bool     CorrectionFlag          { get; init; }
    public Guid?    CorrectsLineId          { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
}
