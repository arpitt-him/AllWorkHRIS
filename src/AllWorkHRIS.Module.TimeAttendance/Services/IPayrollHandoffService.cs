using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public interface IPayrollHandoffService
{
    Task<HandoffResult> ExecuteHandoffAsync(
        Guid payrollPeriodId, Guid payrollRunId, CancellationToken ct = default);
}
