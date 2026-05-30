using AllWorkHRIS.Module.Payroll.Domain.Accumulators;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Unit tests for the reset-boundary math (ADR-020 / Phase 12.9) — pure, no DB.
/// Covers the plan-year correctness that motivated a reset_type-aware design.
/// </summary>
public sealed class ResetBoundaryTests
{
    [Fact]
    public void CalendarYear_PriorBoundary_IsPriorCalendarYear()
    {
        var b = ResetBoundary.Prior("CALENDAR_YEAR", null, null, new DateOnly(2026, 3, 15));

        Assert.Equal(2025, b.Year);
        Assert.Equal(new DateOnly(2025, 1, 1),  b.Start);
        Assert.Equal(new DateOnly(2025, 12, 31), b.End);
        Assert.Equal(new DateOnly(2026, 1, 1),  b.ResetDate);
    }

    [Fact]
    public void PlanYear_StartingJan1_MatchesCalendarYear()
    {
        var b = ResetBoundary.Prior("PLAN_YEAR", 1, 1, new DateOnly(2026, 3, 15));

        Assert.Equal(2025, b.Year);
        Assert.Equal(new DateOnly(2025, 1, 1),  b.Start);
        Assert.Equal(new DateOnly(2025, 12, 31), b.End);
        Assert.Equal(new DateOnly(2026, 1, 1),  b.ResetDate);
    }

    [Fact]
    public void PlanYear_OffCycle_PayDateAfterStart_BoundaryIsPriorPlanYear()
    {
        // Jul 1 plan year; pay date Sep 1 2026 → current plan year started 2026-07-01,
        // so the prior plan year is 2025-07-01 .. 2026-06-30, reset on 2026-07-01.
        var b = ResetBoundary.Prior("PLAN_YEAR", 7, 1, new DateOnly(2026, 9, 1));

        Assert.Equal(2025, b.Year);
        Assert.Equal(new DateOnly(2025, 7, 1),  b.Start);
        Assert.Equal(new DateOnly(2026, 6, 30), b.End);
        Assert.Equal(new DateOnly(2026, 7, 1),  b.ResetDate);
    }

    [Fact]
    public void PlanYear_OffCycle_PayDateBeforeStart_BoundaryIsTwoPlanYearsBack()
    {
        // Jul 1 plan year; pay date Mar 1 2026 is before 2026-07-01, so the current plan
        // year started 2025-07-01 → prior is 2024-07-01 .. 2025-06-30, reset on 2025-07-01.
        var b = ResetBoundary.Prior("PLAN_YEAR", 7, 1, new DateOnly(2026, 3, 1));

        Assert.Equal(2024, b.Year);
        Assert.Equal(new DateOnly(2024, 7, 1),  b.Start);
        Assert.Equal(new DateOnly(2025, 6, 30), b.End);
        Assert.Equal(new DateOnly(2025, 7, 1),  b.ResetDate);
    }
}
