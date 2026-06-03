namespace AllWorkHRIS.Core.Domain.Time;

/// <summary>
/// The regular-vs-overtime hours split for a pay period, derived from daily totals.
/// </summary>
public readonly record struct OvertimeSplit(decimal RegularHours, decimal OvertimeHours)
{
    /// <summary>Total hours across regular and overtime — equals the sum of the inputs.</summary>
    public decimal TotalHours => RegularHours + OvertimeHours;
}

/// <summary>
/// The single authoritative weekly-FLSA overtime split (ADR-023). Pure and deterministic:
/// given a set of daily hour totals, a weekly overtime threshold, and the workweek anchor day,
/// it groups hours into FLSA workweeks and splits each week into regular (up to the threshold)
/// and overtime (the excess), then sums across weeks for the period.
///
/// Both consumers call this one function so the split can never diverge:
/// payroll computes it for pay; Time &amp; Attendance derives it for display. It lives in Core
/// (below both modules) so T&amp;A can use it without a backwards dependency on Payroll (ADR-011).
///
/// The split is <b>never materialized back into</b> <c>time_entry</c> — that table holds real
/// events only; this is the computed view of them.
/// </summary>
public static class OvertimeSplitCalculator
{
    /// <summary>
    /// Computes the period regular/overtime split from per-day hour totals.
    /// </summary>
    /// <param name="dailyHours">
    /// Per-day hour totals (one logical total per work date). Multiple entries for the same
    /// date are summed into that date's workweek, so callers may pass either daily sums or
    /// raw per-entry rows.
    /// </param>
    /// <param name="weeklyOtThresholdHours">Weekly overtime threshold (e.g. 40), uniform across all weeks.</param>
    /// <param name="workWeekStartDay">FLSA workweek anchor: 0=Sunday … 6=Saturday (matches <see cref="DayOfWeek"/>).</param>
    public static OvertimeSplit Compute(
        IEnumerable<(DateOnly WorkDate, decimal Hours)> dailyHours,
        decimal weeklyOtThresholdHours,
        int workWeekStartDay)
        // A uniform threshold is the per-week overload with no per-week entries — every week falls
        // back to the single value. (One code path; the existing callers are unchanged.)
        => Compute(dailyHours, EmptyThresholds, weeklyOtThresholdHours, workWeekStartDay);

    /// <summary>
    /// Per-workweek overload (ADR-024 / Phase 12.13.2): each FLSA workweek is split using the
    /// threshold resolved <b>for that week</b> (effective-dated config), so a pay period spanning a
    /// policy-change boundary evaluates each week under its own policy.
    /// </summary>
    /// <param name="dailyHours">Per-day hour totals; multiple entries per date are summed into that date's week.</param>
    /// <param name="weeklyThresholdByWeekStart">Maps each FLSA week-start (see <see cref="GetWeekStart"/>) to its threshold.</param>
    /// <param name="fallbackThresholdHours">Threshold for any week-start not present in the map (defensive — the caller normally covers every week in the period).</param>
    /// <param name="workWeekStartDay">FLSA workweek anchor (period-stable per ADR-024 D7): 0=Sunday … 6=Saturday.</param>
    public static OvertimeSplit Compute(
        IEnumerable<(DateOnly WorkDate, decimal Hours)> dailyHours,
        IReadOnlyDictionary<DateOnly, decimal> weeklyThresholdByWeekStart,
        decimal fallbackThresholdHours,
        int workWeekStartDay)
    {
        var regHours = 0m;
        var otHours  = 0m;

        // Group by FLSA workweek using the configured anchor, then split each week by ITS threshold.
        foreach (var week in dailyHours.GroupBy(e => GetWeekStart(e.WorkDate, workWeekStartDay)))
        {
            var weekTotal = week.Sum(e => e.Hours);
            var threshold = weeklyThresholdByWeekStart.TryGetValue(week.Key, out var t)
                ? t
                : fallbackThresholdHours;
            regHours += Math.Min(weekTotal, threshold);
            otHours  += Math.Max(weekTotal - threshold, 0m);
        }

        return new OvertimeSplit(regHours, otHours);
    }

    private static readonly IReadOnlyDictionary<DateOnly, decimal> EmptyThresholds =
        new Dictionary<DateOnly, decimal>();

    /// <summary>
    /// Returns the anchor date that starts the FLSA workweek containing <paramref name="date"/>.
    /// </summary>
    /// <param name="weekStartDay">0=Sunday, 1=Monday, … 6=Saturday (matches <see cref="DayOfWeek"/>).</param>
    public static DateOnly GetWeekStart(DateOnly date, int weekStartDay)
    {
        var dow  = (int)date.DayOfWeek;
        var diff = ((dow - weekStartDay) + 7) % 7;
        return date.AddDays(-diff);
    }
}
