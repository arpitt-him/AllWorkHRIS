namespace AllWorkHRIS.Module.Benefits.Domain.Codes;

public sealed record Deduction
{
    public Guid           DeductionId        { get; init; }
    public string         Code               { get; init; } = string.Empty;
    public string         Description        { get; init; } = string.Empty;
    public string         TaxTreatment       { get; init; } = string.Empty;
    public bool           FitExempt          { get; init; }
    public bool           FicaExempt         { get; init; }
    public bool           FutaExempt         { get; init; }
    public bool           SutaExempt         { get; init; }
    public string         Status             { get; init; } = string.Empty;
    public string         CalculationMode    { get; init; } = Codes.CalculationMode.FixedPerPeriod;
    public string?        WageBase           { get; init; }
    public string?        AgeAsOfRule        { get; init; }
    public DateOnly       EffectiveStartDate { get; init; }
    public DateOnly?      EffectiveEndDate   { get; init; }
    public DateTimeOffset CreatedAt          { get; init; }
    public DateTimeOffset UpdatedAt          { get; init; }

    public bool IsActive(DateOnly asOf) =>
        Status == DeductionStatus.Active &&
        EffectiveStartDate <= asOf &&
        (EffectiveEndDate is null || EffectiveEndDate.Value >= asOf);
}
