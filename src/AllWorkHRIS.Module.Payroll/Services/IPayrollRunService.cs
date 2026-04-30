using AllWorkHRIS.Module.Payroll.Commands;
using AllWorkHRIS.Module.Payroll.Domain.Run;

namespace AllWorkHRIS.Module.Payroll.Services;

public interface IPayrollRunService
{
    /// <summary>
    /// Creates a new DRAFT run and enqueues it for background calculation.
    /// Returns the new RunId immediately — caller polls progress via SignalR.
    /// </summary>
    Task<Guid> InitiateRunAsync(InitiatePayrollRunCommand command);

    Task ApproveRunAsync(ApprovePayrollRunCommand command);
    Task ReleaseRunAsync(ReleasePayrollRunCommand command);
    Task CancelRunAsync(CancelPayrollRunCommand command);

    Task<PayrollRun?> GetRunByIdAsync(Guid runId);
    Task<IReadOnlyList<PayrollRun>> GetRunsByContextAsync(Guid payrollContextId);
}
