using AllWorkHRIS.Module.Payroll.Domain.Calendar;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IPayrollContextRepository
{
    Task<PayrollContext?> GetByIdAsync(Guid payrollContextId);
    Task<IReadOnlyList<PayrollContext>> GetAllAsync();
    Task<IReadOnlyList<PayrollContext>> GetAllActiveAsync();
    Task<IReadOnlyList<PayrollContext>> GetByLegalEntityAsync(Guid legalEntityId);
    Task<Guid> InsertContextAsync(PayrollContext context);
    Task UpdateContextStatusAsync(Guid payrollContextId, string status, Guid updatedBy);

    Task<PayrollPeriod?> GetPeriodByIdAsync(Guid periodId);
    Task<PayrollPeriod?> GetCurrentOpenPeriodAsync(Guid payrollContextId);
    Task<IReadOnlyList<PayrollPeriod>> GetOpenPeriodsAsync(Guid payrollContextId);
    Task<IReadOnlyList<PayrollPeriod>> GetPeriodsByContextAsync(Guid payrollContextId, int year);
    Task<Guid> InsertPeriodAsync(PayrollPeriod period);
    /// <summary>
    /// Deletes periods for the given context+year that are not referenced by any payroll run.
    /// Returns (deleted, skipped) — skipped periods have run references and cannot be removed.
    /// </summary>
    Task<(int Deleted, int Skipped)> DeletePeriodsForYearAsync(Guid contextId, int year);
    Task UpdatePeriodStatusAsync(Guid periodId, string status, Guid updatedBy);
}
