using AllWorkHRIS.Module.Payroll.Domain.Calendar;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IPayrollContextRepository
{
    Task<PayrollContext?> GetByIdAsync(Guid payrollContextId);
    Task<IReadOnlyList<PayrollContext>> GetAllAsync();
    Task<IReadOnlyList<PayrollContext>> GetAllActiveAsync();
    Task<IReadOnlyList<PayrollContext>> GetByLegalEntityAsync(Guid legalEntityId);
    Task<Guid> InsertContextAsync(PayrollContext context);
    Task UpdateContextStatusAsync(Guid payrollContextId, string status, Guid updatedBy);
    /// <summary>
    /// Deletes the context and all its OPEN periods if no payroll runs or
    /// CLOSED/LOCKED periods exist. Returns null on success, or a reason string if blocked.
    /// </summary>
    Task<string?> DeleteContextAsync(Guid payrollContextId);

    Task<PayrollPeriod?> GetPeriodByIdAsync(Guid periodId);
    Task<PayrollPeriod?> GetCurrentOpenPeriodAsync(Guid payrollContextId);
    Task<IReadOnlyList<PayrollPeriod>> GetOpenPeriodsAsync(Guid payrollContextId);
    Task<IReadOnlyList<PayrollPeriod>> GetPeriodsByContextAsync(Guid payrollContextId, int year);
    Task<Guid> InsertPeriodAsync(PayrollPeriod period);
    /// <summary>
    /// Deletes periods for the given context+year that are not referenced by any payroll run.
    /// Returns (deleted, skipped) — skipped periods have run references and cannot be removed.
    /// </summary>
    Task<(int Deleted, int Skipped)> DeletePeriodsForYearAsync(Guid contextId, int year);
    Task UpdatePeriodStatusAsync(Guid periodId, string status, Guid updatedBy);
    /// <summary>
    /// Updates the calculation (run) date for a single period. Pass null to clear.
    /// </summary>
    Task UpdatePeriodRunDateAsync(Guid periodId, DateOnly? runDate, Guid updatedBy);

    /// <summary>
    /// Returns the number of pay periods per year for the given payroll context,
    /// derived from the context's pay frequency.
    /// </summary>
    Task<int> GetPeriodsPerYearAsync(Guid payrollContextId);

    /// <summary>
    /// Returns the entity-level OT threshold and workweek start day stored on org_unit,
    /// used to pre-populate new payroll context forms. Returns nulls when the entity
    /// has no stored defaults (system defaults apply: 40.00 hrs, Monday).
    /// </summary>
    Task<(decimal? OtWeeklyThresholdHours, int? WorkweekStartDay, decimal? OtReviewThresholdHours)> GetLegalEntityDefaultsAsync(Guid legalEntityId);

    /// <summary>
    /// Writes an effective-dated OT-config change (OT weekly threshold, workweek start day, OT
    /// review threshold) to <c>payroll_context_ot_config</c> — the source of truth the shared
    /// resolver reads (ADR-024 / Phase 12.13.3). The requested date is snapped forward to a workweek
    /// boundary (D7); the dated interval is maintained (re-edit in place / close the covering row /
    /// bound by the next change) and the scalar columns are re-synced to today's effective values.
    /// Returns the actual (snapped) effective date.
    /// </summary>
    Task<DateOnly> SaveDatedOtConfigAsync(
        Guid payrollContextId, decimal otWeeklyThresholdHours, int workweekStartDay,
        decimal? otReviewThresholdHours, DateOnly requestedEffectiveDate, DateOnly operativeToday, Guid updatedBy);

    /// <summary>
    /// The effective-dated OT-config intervals for a context (ADR-024 / Phase 12.13.3), newest
    /// first — the history + any scheduled future change shown on the pay-calendar detail page.
    /// </summary>
    Task<IReadOnlyList<DatedOtConfigRow>> GetDatedOtConfigHistoryAsync(Guid payrollContextId);
}
