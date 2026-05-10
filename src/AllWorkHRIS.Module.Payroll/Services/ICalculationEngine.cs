using AllWorkHRIS.Module.Payroll.Domain.Results;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed record CalculationInput
{
    public required Guid     EmployeePayrollResultId { get; init; }
    public required Guid     RunId                   { get; init; }
    public required Guid     ResultSetId             { get; init; }
    public required Guid     EmploymentId            { get; init; }
    public required Guid     PersonId                { get; init; }
    public required Guid     PayrollContextId        { get; init; }
    public required Guid     PeriodId                { get; init; }
    public required DateOnly PayDate                 { get; init; }

    // Compensation snapshot — populated by PayrollRunJob before engine is called
    public decimal? AnnualEquivalent { get; init; }
    public int      PeriodsPerYear   { get; init; }

    // Pay period boundaries — passed through to the benefit step provider for proration
    public DateOnly PayPeriodStart    { get; init; }
    public DateOnly PayPeriodEnd      { get; init; }
    public string   PartialPeriodRule { get; init; } = "PRORATE_DAYS";
}

public sealed record CalculationOutput
{
    public required Guid    EmployeePayrollResultId      { get; init; }
    public required bool    Succeeded                    { get; init; }
    public string?          FailureReason                { get; init; }
    public required decimal GrossPay                     { get; init; }
    public required decimal TotalDeductionsAmount        { get; init; }
    public required decimal TotalEmployeeTaxAmount       { get; init; }
    public required decimal TotalEmployerContribAmount   { get; init; }
    public required decimal NetPay                       { get; init; }

    // Set when deductions exceeded available earnings; net pay was floored to $0.
    public bool    NetPayFloorApplied { get; init; }
    public decimal NetPayFloorExcess  { get; init; }
}

public interface ICalculationEngine
{
    /// <summary>
    /// Executes the 9-step ordered computation for a single employee.
    /// Failures are isolated — the result is returned with Succeeded = false
    /// and a failure reason rather than throwing.
    /// </summary>
    Task<CalculationOutput> CalculateAsync(CalculationInput input, CancellationToken ct = default);
}
