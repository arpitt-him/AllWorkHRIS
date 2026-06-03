using AllWorkHRIS.Core.Domain.Time;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Unit tests for the shared weekly-FLSA overtime split (ADR-023 increment 1) — pure, no DB.
/// This is the single authoritative "earned hours" derivation used by both payroll (for pay)
/// and Time &amp; Attendance (for display); these cases pin its behavior so the two cannot diverge.
/// </summary>
public sealed class OvertimeSplitCalculatorTests
{
    private const int Sunday   = 0; // matches DayOfWeek
    private const int Monday   = 1;
    private const decimal Threshold = 40m;

    [Fact]
    public void UnderThreshold_AllRegular_NoOvertime()
    {
        // Mon–Fri, 8h/day = 40h exactly within one Sunday-anchored week → all regular.
        var days = new[]
        {
            (new DateOnly(2026, 6, 1), 8m), // Mon
            (new DateOnly(2026, 6, 2), 8m),
            (new DateOnly(2026, 6, 3), 8m),
            (new DateOnly(2026, 6, 4), 8m),
            (new DateOnly(2026, 6, 5), 8m), // Fri
        };

        var split = OvertimeSplitCalculator.Compute(days, Threshold, Sunday);

        Assert.Equal(40m, split.RegularHours);
        Assert.Equal(0m,  split.OvertimeHours);
        Assert.Equal(40m, split.TotalHours);
    }

    [Fact]
    public void OverThreshold_ExcessIsOvertime()
    {
        // 5×9h = 45h in one week → 40 reg + 5 OT.
        var days = new[]
        {
            (new DateOnly(2026, 6, 1), 9m),
            (new DateOnly(2026, 6, 2), 9m),
            (new DateOnly(2026, 6, 3), 9m),
            (new DateOnly(2026, 6, 4), 9m),
            (new DateOnly(2026, 6, 5), 9m),
        };

        var split = OvertimeSplitCalculator.Compute(days, Threshold, Sunday);

        Assert.Equal(40m, split.RegularHours);
        Assert.Equal(5m,  split.OvertimeHours);
    }

    [Fact]
    public void TwoWeeks_SplitPerWeek_NotAcrossPeriod()
    {
        // Week 1 (Jun 1–5): 45h → 40 reg + 5 OT. Week 2 (Jun 8–12): 30h → 30 reg + 0 OT.
        // A naive period sum (75h) would wrongly yield 35h OT; per-week splitting yields 5h.
        var days = new[]
        {
            (new DateOnly(2026, 6, 1), 9m),
            (new DateOnly(2026, 6, 2), 9m),
            (new DateOnly(2026, 6, 3), 9m),
            (new DateOnly(2026, 6, 4), 9m),
            (new DateOnly(2026, 6, 5), 9m),
            (new DateOnly(2026, 6, 8),  10m),
            (new DateOnly(2026, 6, 9),  10m),
            (new DateOnly(2026, 6, 10), 10m),
        };

        var split = OvertimeSplitCalculator.Compute(days, Threshold, Sunday);

        Assert.Equal(70m, split.RegularHours);
        Assert.Equal(5m,  split.OvertimeHours);
    }

    [Fact]
    public void WeekAnchor_ShiftsWhichDaysGroupTogether()
    {
        // Sat Jun 6 (12h) + Sun Jun 7 (12h) + Mon Jun 8 (40h-worth via 5×8 below).
        // With a SUNDAY anchor, Sat Jun 6 is in the prior week and Sun Jun 7 starts a new week.
        // With a MONDAY anchor, Sat Jun 6 and Sun Jun 7 fall in the SAME week (the one ending Sun),
        // so they sum to 24h together — changing the split.
        var days = new[]
        {
            (new DateOnly(2026, 6, 6), 12m), // Saturday
            (new DateOnly(2026, 6, 7), 12m), // Sunday
        };

        var sundayAnchored = OvertimeSplitCalculator.Compute(days, Threshold, Sunday);
        // Sun-anchored: Sat in week ending Sat (12h, no OT); Sun starts next week (12h, no OT).
        Assert.Equal(24m, sundayAnchored.RegularHours);
        Assert.Equal(0m,  sundayAnchored.OvertimeHours);

        var mondayAnchored = OvertimeSplitCalculator.Compute(days, Threshold, Monday);
        // Mon-anchored: Sat + Sun share the week (Mon Jun 1 .. Sun Jun 7) = 24h, still under 40.
        Assert.Equal(24m, mondayAnchored.RegularHours);
        Assert.Equal(0m,  mondayAnchored.OvertimeHours);
        Assert.Equal(OvertimeSplitCalculator.GetWeekStart(new DateOnly(2026, 6, 6), Monday),
                     OvertimeSplitCalculator.GetWeekStart(new DateOnly(2026, 6, 7), Monday));
    }

    [Fact]
    public void MultipleEntriesSameDay_AreSummedIntoTheWeek()
    {
        // Two punches on the same day are summed; 6+6=12 each day, 4 days = 48h → 40 reg + 8 OT.
        var days = new[]
        {
            (new DateOnly(2026, 6, 1), 6m), (new DateOnly(2026, 6, 1), 6m),
            (new DateOnly(2026, 6, 2), 6m), (new DateOnly(2026, 6, 2), 6m),
            (new DateOnly(2026, 6, 3), 6m), (new DateOnly(2026, 6, 3), 6m),
            (new DateOnly(2026, 6, 4), 6m), (new DateOnly(2026, 6, 4), 6m),
        };

        var split = OvertimeSplitCalculator.Compute(days, Threshold, Sunday);

        Assert.Equal(40m, split.RegularHours);
        Assert.Equal(8m,  split.OvertimeHours);
    }

    [Fact]
    public void Empty_IsZeroSplit()
    {
        var split = OvertimeSplitCalculator.Compute([], Threshold, Sunday);

        Assert.Equal(0m, split.RegularHours);
        Assert.Equal(0m, split.OvertimeHours);
        Assert.Equal(0m, split.TotalHours);
    }

    // ── Per-workweek overload (Phase 12.13.2 / ADR-024): each week split by ITS threshold ──

    [Fact]
    public void PerWeek_EachWeekSplitByItsOwnThreshold()
    {
        // Sunday-anchored: week A starts 2026-05-31, week B starts 2026-06-07.
        var days = new[]
        {
            (new DateOnly(2026, 6, 1), 9m), (new DateOnly(2026, 6, 2), 9m), (new DateOnly(2026, 6, 3), 9m),
            (new DateOnly(2026, 6, 4), 9m), (new DateOnly(2026, 6, 5), 9m),    // week A: 45h
            (new DateOnly(2026, 6, 8), 8m), (new DateOnly(2026, 6, 9), 8m), (new DateOnly(2026, 6, 10), 8m),
            (new DateOnly(2026, 6, 11), 8m), (new DateOnly(2026, 6, 12), 8m),  // week B: 40h
        };
        var byWeek = new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2026, 5, 31)] = 40m, // week A threshold
            [new DateOnly(2026, 6, 7)]  = 35m, // week B threshold (e.g. a CBA / 37.5-hr week)
        };

        var split = OvertimeSplitCalculator.Compute(days, byWeek, fallbackThresholdHours: 40m, Sunday);

        // Week A: 45 → 40 reg + 5 OT.  Week B: 40 → 35 reg + 5 OT.  (A uniform 40 would give 80/5,
        // so reg 75 / OT 10 proves each week used its OWN threshold — the mid-period-change gate.)
        Assert.Equal(75m, split.RegularHours);
        Assert.Equal(10m, split.OvertimeHours);
    }

    [Fact]
    public void PerWeek_WeekStartNotInMap_UsesFallback()
    {
        var days   = new[] { (new DateOnly(2026, 6, 1), 44m) }; // week starting 2026-05-31, 44h
        var byWeek = new Dictionary<DateOnly, decimal>();        // empty → fallback governs

        var split = OvertimeSplitCalculator.Compute(days, byWeek, fallbackThresholdHours: 40m, Sunday);

        Assert.Equal(40m, split.RegularHours);
        Assert.Equal(4m,  split.OvertimeHours);
    }

    [Theory]
    // weekStartDay = Sunday(0): the anchor for any day in Jun 1(Mon)–Jun 6(Sat) is Sun May 31.
    [InlineData(2026, 6, 1, 0, 2026, 5, 31)] // Mon → prior Sun
    [InlineData(2026, 6, 6, 0, 2026, 5, 31)] // Sat → same prior Sun
    [InlineData(2026, 6, 7, 0, 2026, 6, 7)]  // Sun → itself
    // weekStartDay = Monday(1): the anchor for Jun 1(Mon)–Jun 7(Sun) is Mon Jun 1.
    [InlineData(2026, 6, 1, 1, 2026, 6, 1)]  // Mon → itself
    [InlineData(2026, 6, 7, 1, 2026, 6, 1)]  // Sun → prior Mon
    public void GetWeekStart_AnchorsToConfiguredDay(
        int y, int m, int d, int startDay, int ey, int em, int ed)
    {
        var anchor = OvertimeSplitCalculator.GetWeekStart(new DateOnly(y, m, d), startDay);
        Assert.Equal(new DateOnly(ey, em, ed), anchor);
    }
}
