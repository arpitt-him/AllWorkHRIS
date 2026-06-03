using AllWorkHRIS.Core.Domain.Time;

namespace AllWorkHRIS.Core.Composition;

/// <summary>
/// Thin discovery interface placed in Core so HRIS / Time &amp; Attendance can reach payroll
/// context data without taking a direct dependency on the Payroll module.
/// Implemented by the Payroll module; absent (Null fallback) if the module is not loaded.
/// </summary>
public interface IPayrollContextLookup
{
    Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsAsync();
    Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsByLegalEntityAsync(Guid legalEntityId);

    /// <summary>
    /// Resolve the effective-dated overtime configuration for a payroll context as-of a date
    /// (ADR-024). The single resolver shared by payroll (pay) and T&amp;A (display). Returns the
    /// dated row whose window covers <paramref name="asOf"/>; falls back to the system default
    /// (40 hrs / Monday / no review trigger) if the context has no config row.
    /// </summary>
    Task<OtConfig> ResolveOtConfigAsync(Guid payrollContextId, DateOnly asOf);

    /// <summary>
    /// Resolve the OT configuration for an entire pay period (ADR-024 / Phase 12.13.2): the
    /// (period-stable, D7) anchor resolved at period start, plus a per-FLSA-week threshold map
    /// across <paramref name="periodStart"/>..<paramref name="periodEnd"/>. The single per-period
    /// resolver shared by payroll and T&amp;A; lets each workweek be split under its own threshold.
    /// </summary>
    Task<PeriodOtConfig> ResolveOtConfigForPeriodAsync(Guid payrollContextId, DateOnly periodStart, DateOnly periodEnd);

    /// <summary>
    /// Resolve the set of time-category ids that COUNT TOWARD overtime for a payroll context as-of a
    /// date (ADR-024 / Phase 12.13.4) — the effective-dated, per-context override of the OT basis.
    /// Resolved as-of period start (period-stable, like the anchor). An <b>empty</b> result means
    /// "no override": callers fall back to the <c>lkp_time_category.is_worked_time</c> flag (today's
    /// behavior). A non-empty set partitions payable hours — members feed the FLSA threshold, the
    /// rest are paid at straight time.
    /// </summary>
    Task<IReadOnlyCollection<int>> ResolveOtEligibleCategoriesAsync(Guid payrollContextId, DateOnly asOf);
}
