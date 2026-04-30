using AllWorkHRIS.Module.Payroll.Domain.Results;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed record CalculationInput
{
    public required Guid     EmployeePayrollResultId { get; init; }
    public required Guid     RunId                   { get; init; }
    public required Guid     ResultSetId             { get; init; }
    public required Guid     EmploymentId            { get; init; }
    public required Guid     PersonId                { get; init; }
    public required Guid     PeriodId                { get; init; }
    public required DateOnly PayDate                 { get; init; }
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
