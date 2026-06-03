namespace AllWorkHRIS.Core.Domain.Time;

/// <summary>
/// The overtime configuration resolved for an entire pay period (ADR-024 / Phase 12.13.2):
/// the (period-stable, D7) workweek anchor, a <b>per-FLSA-week</b> threshold map, the fallback
/// threshold for any week not in the map, and the period's review trigger. Built once by
/// <c>IPayrollContextLookup.ResolveOtConfigForPeriodAsync</c> and consumed by both the payroll
/// engine (for pay) and Time &amp; Attendance (for display) — so a period spanning a policy-change
/// boundary splits each workweek under its own threshold, identically on both sides.
/// </summary>
public sealed record PeriodOtConfig(
    int                                    WorkweekStartDay,
    IReadOnlyDictionary<DateOnly, decimal> WeeklyThresholdByWeekStart,
    decimal                                FallbackThresholdHours,
    decimal?                               ReviewThresholdHours);
