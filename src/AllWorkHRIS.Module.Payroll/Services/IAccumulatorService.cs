using AllWorkHRIS.Module.Payroll.Domain.Results;

namespace AllWorkHRIS.Module.Payroll.Services;

public interface IAccumulatorService
{
    /// <summary>
    /// Applies the 4-layer accumulator mutation chain for a completed employee result.
    /// Must be called inside the same transaction as the result line inserts.
    /// </summary>
    Task ApplyAsync(EmployeePayrollResult result, Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Reverses all accumulator impacts associated with a given payroll result.
    /// Used during correction and reprocessing flows.
    /// </summary>
    Task ReverseAsync(Guid employeePayrollResultId, Guid reversedBy, CancellationToken ct = default);
}
