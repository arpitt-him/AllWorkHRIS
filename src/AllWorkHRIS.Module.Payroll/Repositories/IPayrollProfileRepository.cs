using AllWorkHRIS.Module.Payroll.Domain.Profile;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IPayrollProfileRepository
{
    Task<Guid>                          InsertAsync(PayrollProfile profile);
    Task<PayrollProfile?>               GetByEmploymentIdAsync(Guid employmentId);
    Task<IReadOnlyList<PayrollProfile>> GetByContextAsync(Guid payrollContextId);
    Task<IReadOnlyList<Guid>>           GetActiveEmploymentIdsByContextAsync(Guid payrollContextId);
    Task<int>                           CountActiveByContextAsync(Guid payrollContextId);
    Task                        UpdateStatusAsync(Guid employmentId, string status, Guid updatedBy);
    Task                        SetFinalPayFlagAsync(Guid employmentId, bool finalPayFlag, Guid updatedBy);
    Task                        SetBlockingTasksClearedAsync(Guid employmentId, Guid updatedBy);
    Task<IReadOnlyList<Guid>>   GetActiveBlockedEmploymentIdsByContextAsync(Guid payrollContextId);
}
