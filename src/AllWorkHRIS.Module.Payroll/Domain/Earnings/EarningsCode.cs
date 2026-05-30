namespace AllWorkHRIS.Module.Payroll.Domain.Earnings;

/// <summary>
/// Config row defining an earnings code the calculation engine may emit
/// (Phase 12.5.4). The engine validates every emitted code against this set and
/// reads <see cref="TaxableFlag"/> / <see cref="AccumulatorImpactFlag"/> from it
/// instead of hardcoding; the YTD accumulator is resolved through the explicit
/// <see cref="AccumulatorDefinitionId"/> link (null = does not accumulate).
/// Mirrors the deduction-side <c>benefit_deduction</c> model.
/// </summary>
public sealed record EarningsCode
{
    public Guid           EarningsCodeId          { get; init; }
    public string         Code                    { get; init; } = default!;
    public string         Description             { get; init; } = default!;
    public string         EarningsCategory        { get; init; } = default!; // REGULAR | OVERTIME | SUPPLEMENTAL | OTHER
    public bool           TaxableFlag             { get; init; }
    public bool           AccumulatorImpactFlag   { get; init; }
    public Guid?          AccumulatorDefinitionId { get; init; }
    public string         Status                  { get; init; } = default!; // ACTIVE | INACTIVE
    public DateOnly       EffectiveStartDate      { get; init; }
    public DateOnly?      EffectiveEndDate        { get; init; }
    public string         CreatedBy               { get; init; } = default!;
    public DateTimeOffset CreatedAt               { get; init; }
    public string         LastUpdatedBy           { get; init; } = default!;
    public DateTimeOffset UpdatedAt               { get; init; }
}
