using AllWorkHRIS.Module.Payroll.Domain.Run;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IPayrollRunRepository
{
    Task<PayrollRun?> GetByIdAsync(Guid runId);
    Task<IReadOnlyList<PayrollRun>> GetByContextAsync(Guid payrollContextId);
    Task<bool> HasOpenRunForPeriodAsync(Guid payrollContextId, Guid periodId);
    Task<Guid> InsertAsync(PayrollRun run);
    Task UpdateStatusAsync(Guid runId, int statusId, Guid updatedBy);
    Task SetRunTimestampsAsync(Guid runId, DateTimeOffset startTimestamp, DateTimeOffset? endTimestamp, Guid updatedBy);
}
