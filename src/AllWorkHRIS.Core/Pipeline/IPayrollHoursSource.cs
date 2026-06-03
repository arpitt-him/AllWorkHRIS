namespace AllWorkHRIS.Core.Pipeline;

/// <summary>
/// Source of approved/locked worked hours used by the payroll calculation engine
/// to compute hours-based pay and FLSA overtime for non-exempt employees.
///
/// Placed in Core so Payroll can consume hours without taking a direct assembly
/// reference on the Time &amp; Attendance module. Implemented by the T&amp;A module
/// when loaded; falls back to <see cref="NullPayrollHoursSource"/> (empty result)
/// when T&amp;A is not in the composition.
/// </summary>
public interface IPayrollHoursSource
{
    /// <summary>
    /// Returns total <b>worked</b> hours (categories flagged <c>is_worked_time</c>, e.g. REGULAR/
    /// OVERTIME) per calendar date for an employment within the pay-period date range, for the run
    /// identified by <paramref name="payrollRunId"/>. These are the hours that count toward the FLSA
    /// weekly overtime threshold (Phase 12.12 / ADR-023); paid leave is excluded here and summed via
    /// <see cref="GetApprovedNonWorkedPayableHoursByEmploymentAndPeriodAsync"/>. Includes hours that
    /// are APPROVED (not yet committed to any run) or already LOCKED to <em>this</em> run; excludes
    /// hours LOCKED to a <em>different</em> run, so a second run over the same date range can't
    /// re-sum hours an earlier approved run already consumed (Phase 12.7).
    /// </summary>
    Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId);

    /// <summary>
    /// Returns the total <b>non-worked but payable</b> hours (paid-leave categories — PTO, holiday,
    /// sick — flagged <c>payable</c> but not <c>is_worked_time</c>) for an employment within the
    /// pay-period date range, under the same APPROVED / LOCKED-to-this-run rule as
    /// <see cref="GetApprovedHoursByEmploymentAndPeriodAsync"/>. These are paid at straight time and
    /// do <b>not</b> count toward the FLSA overtime threshold; <c>UNPAID</c> is excluded. (Phase 12.12.)
    /// </summary>
    Task<decimal> GetApprovedNonWorkedPayableHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId);

    /// <summary>
    /// Phase 12.7 — lock-on-approve: when a run is approved, lock the APPROVED time entries that
    /// fed it (those of <paramref name="employmentIds"/> within the period range) to the run
    /// (APPROVED → LOCKED, stamped with the run id). "Approve = commit YTD and lock the hours
    /// that produced it." Idempotent: entries already locked to this run are left as-is.
    /// </summary>
    Task LockHoursForRunAsync(
        Guid payrollRunId, IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    /// <summary>
    /// Phase 12.7 — unlock-on-cancel/reopen: release every time entry locked to the run
    /// (LOCKED → APPROVED, clear the run id), so the hours return to the pool for a fresh run.
    /// Closes the stranded-lock gap.
    /// </summary>
    Task UnlockHoursForRunAsync(Guid payrollRunId, CancellationToken ct = default);
}
