namespace AllWorkHRIS.Module.Benefits.Domain.Codes;

public sealed record DeductionRateTable
{
    public Guid           RateTableId   { get; init; }
    public Guid           DeductionId   { get; init; }
    public string         RateType      { get; init; } = string.Empty;  // COVERAGE_TIER | AGE_BAND | SALARY_BAND
    public DateOnly       EffectiveFrom { get; init; }
    public DateOnly?      EffectiveTo   { get; init; }
    public string?        Description   { get; init; }
    public DateTimeOffset CreatedAt     { get; init; }
}
