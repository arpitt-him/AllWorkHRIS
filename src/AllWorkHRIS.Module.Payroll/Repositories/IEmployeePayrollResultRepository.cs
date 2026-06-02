using AllWorkHRIS.Module.Payroll.Domain.Results;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IEmployeePayrollResultRepository
{
    Task<EmployeePayrollResult?> GetByIdAsync(Guid resultId);
    Task<IReadOnlyList<EmployeePayrollResult>> GetByResultSetIdAsync(Guid resultSetId);
    Task<IReadOnlyList<EmployeePayrollResult>> GetByRunIdAsync(Guid runId);

    // ToDo #43 — distinct employment_ids that already have a *standing posted* result
    // (status in paidStatusIds: APPROVED / RELEASED / FINALIZED) for the given execution
    // period. Used to block / drop already-paid targets from an additive scoped run so it
    // can't double-pay. REVERSED / CORRECTED are intentionally excluded — those are what a
    // correction produces, not a standing payment.
    Task<IReadOnlyList<Guid>> GetPaidEmploymentIdsForPeriodAsync(Guid periodId, IReadOnlyCollection<int> paidStatusIds);

    Task<Guid> InsertAsync(EmployeePayrollResult result);
    Task UpdateStatusAsync(Guid resultId, int statusId);
    Task UpdateTotalsAsync(Guid resultId, decimal grossPay, decimal totalDeductions,
        decimal totalEmployeeTax, decimal totalEmployerContribution, decimal netPay);
}
