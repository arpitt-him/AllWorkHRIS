namespace AllWorkHRIS.Module.Tax.Queries;

public sealed record BracketRow(decimal LowerLimit, decimal? UpperLimit, decimal Rate);

public sealed record TieredBracketRow(
    decimal LowerLimit, decimal? UpperLimit, decimal Rate,
    decimal? PeriodCap, decimal? AnnualCap);

public sealed record FlatRateRow(
    decimal Rate, decimal? WageBase, decimal? PeriodCap, decimal? AnnualCap,
    string? DependsOnStepCode);

public sealed record AllowanceRow(decimal AnnualAmount);

public sealed record CreditRow(decimal AnnualAmount, decimal CreditRate, bool IsRefundable);

// Employee filing profile — loaded from employee_tax_form_submission + employee_tax_form_detail
public sealed record EmployeeFilingProfile
{
    public string? FilingStatusCode      { get; init; }
    public int     AllowanceCount        { get; init; }
    public decimal AdditionalWithholding { get; init; }
    public bool    ExemptFlag            { get; init; }
    public bool    IsLegacyForm          { get; init; }
    public decimal OtherIncomeAmount     { get; init; }
    public decimal DeductionsAmount      { get; init; }
    public decimal CreditsAmount         { get; init; }
    public int?    ClaimCode             { get; init; }
    public decimal TotalClaimAmount      { get; init; }
    public decimal AdditionalTaxAmount   { get; init; }
}

// Step row loaded from payroll_calculation_steps
public sealed record CalculationStepRow
{
    public required string StepCode             { get; init; }
    public required string StepType             { get; init; }
    public required string CalculationCategory  { get; init; }
    public required int    SequenceNumber        { get; init; }
    public required string AppliesTo            { get; init; }
    public required bool   ReducesIncomeTaxWages { get; init; }
    public required bool   ReducesFicaWages      { get; init; }
}
