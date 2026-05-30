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

    /// <summary>
    /// Re-enqueue a run stuck in an idempotent transient state (APPROVING or
    /// RELEASING) so the background job picks it up again without a host
    /// restart. The on-demand twin of PayrollRunJob's startup recovery.
    /// </summary>
    Task ResumeRunAsync(ResumePayrollRunCommand command);

    Task<PayrollRun?> GetRunByIdAsync(Guid runId);
    Task<IReadOnlyList<PayrollRun>> GetRunsByContextAsync(Guid payrollContextId);
}
