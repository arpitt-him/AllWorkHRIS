using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Repositories;

public interface ITimeEntryRepository
{
    Task<TimeEntry?>              GetByIdAsync(Guid timeEntryId);
    Task<IEnumerable<TimeEntry>>  GetByEmploymentAndPeriodAsync(Guid employmentId, Guid payrollPeriodId);
    Task<IEnumerable<TimeEntry>>  GetPendingApprovalByManagerAsync(Guid managerEmploymentId, Guid payrollPeriodId);
    Task<IEnumerable<TimeEntry>>  GetApprovedForHandoffAsync(Guid payrollPeriodId);
    Task<IEnumerable<TimeEntry>>  GetOpenByEmploymentAsync(Guid employmentId);
    Task<Guid>                    InsertAsync(TimeEntry entry, IUnitOfWork uow);
    Task                          UpdateStatusAsync(Guid timeEntryId, string status, Guid actorId, IUnitOfWork uow);
    Task                          UpdateStatusWithReasonAsync(Guid timeEntryId, string status, Guid actorId, string reason, IUnitOfWork uow);

    // Phase 12.7b — approval at scale.
    /// <summary>Batch-approve SUBMITTED/CORRECTED entries (by id) to APPROVED; returns the count actually transitioned.</summary>
    Task<int>                     ApproveEntriesAsync(IReadOnlyList<Guid> timeEntryIds, Guid approvedBy, IUnitOfWork uow);
    /// <summary>Per-LE config: whether IMPORT-method time entries auto-approve on submit.</summary>
    Task<bool>                    GetAutoApproveImportedTimeAsync(Guid legalEntityId);
    Task                          LockAsync(Guid timeEntryId, Guid payrollRunId, DateTimeOffset lockedAt, IUnitOfWork uow);

    // Phase 12.7 — lock-on-approve / unlock-on-cancel. These wrap their own unit of work since
    // they are driven from the payroll approval/cancel path via the Core IPayrollHoursSource seam.
    /// <summary>Locks the APPROVED entries of the given employments within the period range to the
    /// run (APPROVED → LOCKED + run id). Idempotent; returns the number of entries locked.</summary>
    Task<int>                     LockHoursForRunAsync(Guid payrollRunId, IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);
    /// <summary>Releases every entry locked to the run (LOCKED → APPROVED, clear run id). Returns the count.</summary>
    Task<int>                     UnlockHoursForRunAsync(Guid payrollRunId, CancellationToken ct = default);

    /// <summary>ADR-027 2a.3 — release only the selected employees' entries locked to this run
    /// (LOCKED → APPROVED), so a per-employee reversal frees just those employees' hours.</summary>
    Task<int>                     UnlockHoursForEmploymentsInRunAsync(
        Guid payrollRunId, IReadOnlyCollection<Guid> employmentIds, CancellationToken ct = default);
    Task<bool>                    EmploymentExistsAsync(Guid employmentId);
    Task<string?>                 GetPeriodStatusAsync(Guid payrollPeriodId);
    Task<string?>                 GetFlsaStatusAsync(Guid employmentId);
    Task<bool>                    IsCategoryWorkedTimeAsync(string categoryCode);

    /// <summary>
    /// Returns total approved/locked worked hours per calendar date for an employment within a
    /// pay period date range. Used by the payroll engine to compute hours-based pay and FLSA
    /// overtime for non-exempt employees.
    /// </summary>
    Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId,
        IReadOnlyCollection<int> otEligibleCategoryIds);

    /// <summary>
    /// Total approved/locked <b>non-worked but payable</b> hours (paid leave — `payable` and NOT
    /// OT-eligible) for an employment within the period; paid at straight time, excluded from the
    /// FLSA overtime threshold (Phase 12.12). `UNPAID` is excluded. Phase 12.13.4: the non-eligible
    /// set is the complement of <paramref name="otEligibleCategoryIds"/> over payable categories;
    /// an empty set falls back to "payable and not `is_worked_time`".
    /// </summary>
    Task<decimal> GetApprovedNonWorkedPayableHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId,
        IReadOnlyCollection<int> otEligibleCategoryIds);
}
