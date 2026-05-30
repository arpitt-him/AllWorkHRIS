namespace AllWorkHRIS.Module.Payroll.Domain.Accumulators;

/// <summary>
/// The reset boundary immediately preceding a pay date, for an accumulator's reset
/// semantics (ADR-020 / Phase 12.9). <see cref="Year"/> identifies the closed boundary
/// by its start year; <see cref="Start"/>..<see cref="End"/> is its date range;
/// <see cref="ResetDate"/> is when it reset (i.e. when the next boundary opened).
/// </summary>
public readonly record struct ResetBoundaryInfo(int Year, DateOnly Start, DateOnly End, DateOnly ResetDate);

/// <summary>Pure reset-boundary math — no I/O, no clock — so it is directly unit-testable.</summary>
public static class ResetBoundary
{
    /// <summary>
    /// The prior reset boundary relative to <paramref name="payDate"/>.
    /// <c>PLAN_YEAR</c> uses <paramref name="planYearStartMonth"/>/<paramref name="planYearStartDay"/>
    /// (defaulting to Jan 1); anything else is treated as a calendar year.
    /// </summary>
    public static ResetBoundaryInfo Prior(string resetType, int? planYearStartMonth, int? planYearStartDay, DateOnly payDate)
    {
        if (resetType == "PLAN_YEAR")
        {
            var month = planYearStartMonth ?? 1;
            var day   = planYearStartDay   ?? 1;

            var startThisYear    = SafeDate(payDate.Year, month, day);
            var currentStartYear = payDate >= startThisYear ? payDate.Year : payDate.Year - 1;
            var priorStartYear   = currentStartYear - 1;

            var priorStart   = SafeDate(priorStartYear,   month, day);
            var currentStart = SafeDate(currentStartYear, month, day);
            return new ResetBoundaryInfo(priorStartYear, priorStart, currentStart.AddDays(-1), currentStart);
        }

        // CALENDAR_YEAR (default): prior calendar year, reset effective Jan 1 of the pay year.
        var priorYear = payDate.Year - 1;
        return new ResetBoundaryInfo(
            priorYear,
            new DateOnly(priorYear, 1, 1),
            new DateOnly(priorYear, 12, 31),
            new DateOnly(payDate.Year, 1, 1));
    }

    // Calendar arithmetic only (not a clock read) — clamps the day to the month length.
    private static DateOnly SafeDate(int year, int month, int day)
        => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
