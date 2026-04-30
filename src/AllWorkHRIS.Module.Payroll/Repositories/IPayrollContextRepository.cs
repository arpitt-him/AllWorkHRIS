using AllWorkHRIS.Module.Payroll.Domain.Calendar;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IPayrollContextRepository
{
    Task<PayrollContext?> GetByIdAsync(Guid payrollContextId);
    Task<IReadOnlyList<PayrollContext>> GetAllActiveAsync();
    Task<IReadOnlyList<PayrollContext>> GetByLegalEntityAsync(Guid legalEntityId);
    Task<Guid> InsertContextAsync(PayrollContext context);
    Task UpdateContextStatusAsync(Guid payrollContextId, string status, Guid updatedBy);

    Task<PayrollPeriod?> GetPeriodByIdAsync(Guid periodId);
    Task<PayrollPeriod?> GetCurrentOpenPeriodAsync(Guid payrollContextId);
    Task<IReadOnlyList<PayrollPeriod>> GetPeriodsByContextAsync(Guid payrollContextId, int year);
    Task<Guid> InsertPeriodAsync(PayrollPeriod period);
    Task UpdatePeriodStatusAsync(Guid periodId, string status, Guid updatedBy);
}
