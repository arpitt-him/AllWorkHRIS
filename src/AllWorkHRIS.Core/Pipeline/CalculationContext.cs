using System.Collections.Immutable;

namespace AllWorkHRIS.Core.Pipeline;

public sealed record CalculationContext
{
    // Identity
    public Guid     EmployeeId        { get; init; }
    public Guid     PayrollContextId  { get; init; }
    public Guid     PeriodId          { get; init; }
    public DateOnly PayDate           { get; init; }
    public int      PayPeriodsPerYear { get; init; }

    // Pay period boundaries
    public DateOnly PayPeriodStart         { get; init; }
    public DateOnly PayPeriodEnd           { get; init; }
    public int      PayDatesInPeriodMonth  { get; init; } = 2;
    public int      PayDateOrdinalInMonth  { get; init; } = 1;
    public string   PartialPeriodRule      { get; init; } = "PRORATE_DAYS";
    public string   ThreePaycheckMonthRule { get; init; } = "PRORATE";

    // Jurisdiction
    public string JurisdictionCode { get; init; } = string.Empty;

    // Wage figures — IncomeTaxableWages and FicaTaxableWages start equal to GrossPayPeriod
    // and are independently reduced by pre-tax steps according to the step's wage-base flags.
    public decimal GrossPayPeriod     { get; init; }  // unchanged throughout
    public decimal AnnualizedGross    { get; init; }  // GrossPayPeriod × PayPeriodsPerYear
    public decimal IncomeTaxableWages { get; init; }  // read by ProgressiveBracketStep and income-tax FlatRateStep
    public decimal FicaTaxableWages   { get; init; }  // read by social-insurance FlatRateStep and TieredFlatStep
    public decimal DisposableIncome   { get; init; }  // set after Tax phase; used by garnishment steps (Phase 6+)

    // Running totals
    public decimal ComputedTax  { get; init; }
    public decimal NetPay       { get; init; }
    public decimal EmployerCost { get; init; }  // does not reduce NetPay

    // Named results for cross-step reads (e.g. PercentageOfPriorResultStep)
    // Contains BOTH employee and employer steps so PercentageOfPrior can reference either.
    public ImmutableDictionary<string, decimal> StepResults { get; init; }
        = ImmutableDictionary<string, decimal>.Empty;

    // Employer contribution results — subset of StepResults, for output separation
    public ImmutableDictionary<string, decimal> EmployerStepResults { get; init; }
        = ImmutableDictionary<string, decimal>.Empty;

    // YTD accumulator balances for cap enforcement
    public ImmutableDictionary<string, decimal> YtdBalances { get; init; }
        = ImmutableDictionary<string, decimal>.Empty;

    // Common promoted columns — populated from employee_tax_form_detail for the active submission
    public string? FilingStatusCode      { get; init; }
    public int     AllowanceCount        { get; init; }
    public decimal AdditionalWithholding { get; init; }
    public bool    ExemptFlag            { get; init; }
    public bool    IsLegacyForm          { get; init; }  // TRUE for pre-2020 W-4 submissions

    // Form-specific columns — zero/null when not applicable to the employee's form type
    public decimal OtherIncomeAmount { get; init; }  // W-4 2020+ Step 4a
    public decimal DeductionsAmount  { get; init; }  // W-4 2020+ Step 4b
    public decimal CreditsAmount     { get; init; }  // W-4 2020+ Step 3
    public int?    ClaimCode         { get; init; }  // TD1 CRA standard code 1–10
    public decimal TotalClaimAmount  { get; init; }  // TD1 custom worksheet total

    // Mutators

    public CalculationContext WithDisposableIncome(decimal disposableIncome)
        => this with { DisposableIncome = disposableIncome };

    public CalculationContext WithReducedIncomeTaxableWages(decimal reduction)
        => this with { IncomeTaxableWages = Math.Max(0, IncomeTaxableWages - reduction) };

    public CalculationContext WithReducedFicaTaxableWages(decimal reduction)
        => this with { FicaTaxableWages = Math.Max(0, FicaTaxableWages - reduction) };

    public CalculationContext WithStepResult(string stepCode, decimal amount)
        => this with
        {
            StepResults = StepResults.SetItem(stepCode, amount),
            ComputedTax = ComputedTax + amount,
            NetPay      = NetPay - amount
        };

    public CalculationContext WithEmployerStepResult(string stepCode, decimal amount)
        => this with
        {
            StepResults         = StepResults.SetItem(stepCode, amount),
            EmployerStepResults = EmployerStepResults.SetItem(stepCode, amount),
            EmployerCost        = EmployerCost + amount
        };
}
