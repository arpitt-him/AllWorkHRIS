using AllWorkHRIS.Module.Payroll.Domain.Results;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IEmployeePayrollResultRepository
{
    Task<EmployeePayrollResult?> GetByIdAsync(Guid resultId);
    Task<IReadOnlyList<EmployeePayrollResult>> GetByResultSetIdAsync(Guid resultSetId);
    Task<IReadOnlyList<EmployeePayrollResult>> GetByRunIdAsync(Guid runId);
    Task<Guid> InsertAsync(EmployeePayrollResult result);
    Task UpdateStatusAsync(Guid resultId, int statusId);
    Task UpdateTotalsAsync(Guid resultId, decimal grossPay, decimal totalDeductions,
        decimal totalEmployeeTax, decimal totalEmployerContribution, decimal netPay);
}
