namespace AllWorkHRIS.Core.Domain.Time;

/// <summary>
/// The overtime configuration in effect for a payroll context as-of a date (ADR-024):
/// the weekly FLSA threshold, the FLSA workweek anchor, and the approve-by-exception review
/// trigger. Resolved via <c>IPayrollContextLookup.ResolveOtConfigAsync</c> and consumed by
/// <b>both</b> the payroll engine (for pay) and Time &amp; Attendance (for display) — one
/// source, two consumers (ADR-024 D4). The OT-eligible category set joins this record in 12.13.4.
/// </summary>
public sealed record OtConfig(
    decimal  WeeklyThresholdHours,
    int      WorkweekStartDay,
    decimal? ReviewThresholdHours);
