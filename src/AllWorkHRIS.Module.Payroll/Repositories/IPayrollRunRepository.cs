using AllWorkHRIS.Module.Payroll.Domain.Run;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IPayrollRunRepository
{
    Task<PayrollRun?> GetByIdAsync(Guid runId);
    Task<IReadOnlyList<PayrollRun>> GetByContextAsync(Guid payrollContextId);
    /// <summary>
    /// Per ADR-017 §5: returns the in-flight run for this payroll context, if any.
    /// "In flight" means an un-approved run still moving through the lifecycle —
    /// status IN (DRAFT, CALCULATING, CALCULATED, APPROVING). Returns null if no
    /// such run exists. Approved / Releasing / Released / Closed / Cancelled /
    /// Failed runs are NOT in flight and do NOT block — that's deliberate, since
    /// approval is the gate that commits YTD and unlocks the next run, and
    /// supplemental/catch-up runs are explicitly allowed for a period that
    /// already has an approved run (per ADR-017 §4b/§4d).
    ///
    /// Scoped per payroll context (not per period) so independent pay groups
    /// (weekly hourly vs biweekly salaried vs monthly execs) never block each
    /// other.
    /// </summary>
    Task<PayrollRun?> GetInFlightRunForContextAsync(Guid payrollContextId);

    /// <summary>
    /// Returns the existing Regular-type run for a period if one exists in any
    /// non-terminal-failure status (i.e. anything except Cancelled/Failed).
    /// Used to enforce "one Regular run per period" (ADR-017 §4b) — Supplemental
    /// / Adjustment / Correction runs are unaffected and may still be added to a
    /// period that already has an approved Regular run.
    /// </summary>
    Task<PayrollRun?> GetActiveRegularRunForPeriodAsync(Guid periodId);

    /// <summary>
    /// Host-startup recovery (ADR-017 / Phase 12.5.3): runs left in a transient
    /// state (CALCULATING / APPROVING / RELEASING) by a hard restart, whose
    /// in-memory queue entry was lost. The background job re-enqueues the
    /// idempotent ones (APPROVING / RELEASING) and fails an interrupted
    /// CALCULATING run. Matched by status code, not id.
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> GetRunsInTransientStatesAsync();
    Task<Guid> InsertAsync(PayrollRun run);
    Task UpdateStatusAsync(Guid runId, int statusId, Guid updatedBy);
    Task SetRunTimestampsAsync(Guid runId, DateTimeOffset startTimestamp, DateTimeOffset? endTimestamp, Guid updatedBy);
    Task InsertRunExceptionAsync(PayrollRunException exception);
    Task<IReadOnlyList<PayrollRunException>> GetRunExceptionsAsync(Guid runId);
}
