using AllWorkHRIS.Module.Payroll.Commands;
using AllWorkHRIS.Module.Payroll.Domain.Run;
using AllWorkHRIS.Module.Payroll.Repositories;

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

    // Phase 12.6 — scoped/targeted runs.
    /// <summary>Active employees in a context (EE # + name) for the scoped-run targeting picker.</summary>
    Task<IReadOnlyList<RunTargetEmployee>> GetTargetableEmployeesAsync(Guid payrollContextId);
    /// <summary>The distinct employment ids flagged as exceptions on the period's Regular run —
    /// pre-fills the picker for the "carry over this period's exceptions" catch-up case. Empty if none.</summary>
    Task<IReadOnlyList<Guid>> GetCarryoverEmploymentIdsAsync(Guid periodId);
    /// <summary>ToDo #43 — employment ids already paid (standing posted result) for the period, so
    /// the picker can badge / block them: an additive scoped run must not re-pay an already-paid EE.</summary>
    Task<IReadOnlyList<Guid>> GetAlreadyPaidEmploymentIdsAsync(Guid periodId);
    /// <summary>The run_scope for a scoped run (for "Scoped: N EEs" surfacing), or null.</summary>
    Task<RunScope?> GetRunScopeAsync(Guid runScopeId);
}
