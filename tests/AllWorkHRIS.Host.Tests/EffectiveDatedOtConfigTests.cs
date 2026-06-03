using Dapper;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Domain.Time;
using AllWorkHRIS.Module.Payroll.Repositories;
using AllWorkHRIS.Module.Payroll.Services;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Phase 12.13.1 spot-check (ADR-024). Exercises the <b>real</b>
/// <see cref="PayrollContextLookup.ResolveOtConfigAsync"/> against the effective-dated
/// <c>payroll_context_ot_config</c> table — the path the gate suite skips (it registers the
/// Null lookup in the test container). Proves migration 048 backfilled an open-ended row and that
/// the resolver reads it, returning values identical to the <c>payroll_context</c> scalar columns
/// (parity — no behavior change). Requires allworkhris_dev with migration 048 applied.
/// </summary>
public sealed class EffectiveDatedOtConfigTests
{
    // CORP-BW gate fixture context (payroll_gate_test_fixture.sql).
    static readonly Guid CorpBwContextId = Guid.Parse("dd6ee25f-e02c-498e-8bd3-e5f397b40f4f");

    readonly IConnectionFactory _connectionFactory;

    public EffectiveDatedOtConfigTests()
    {
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
            "Host=localhost;Database=allworkhris_dev;Username=postgres;Password=dev");
        Environment.SetEnvironmentVariable("DATABASE_PROVIDER", "postgresql");
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new TestDateOnlyHandler());
        SqlMapper.AddTypeHandler(new TestNullableDateOnlyHandler());
        _connectionFactory = new ConnectionFactory();
    }

    [Fact]
    public async Task ResolveOtConfig_ReadsBackfilledRow_MatchingContextScalars()
    {
        using var conn = _connectionFactory.CreateConnection();

        // The context's scalar columns (the cache) — the values the backfill copied.
        var scalar = await conn.QueryFirstOrDefaultAsync<OtScalarRow>(
            """
            SELECT ot_weekly_threshold_hours, workweek_start_day, ot_review_threshold_hours
            FROM   payroll_context WHERE payroll_context_id = @Id
            """,
            new { Id = CorpBwContextId });
        Assert.NotNull(scalar); // CORP-BW fixture must be present

        // Migration 048 must have backfilled exactly one open-ended dated row for the context.
        var datedRowCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM payroll_context_ot_config WHERE payroll_context_id = @Id AND end_date IS NULL",
            new { Id = CorpBwContextId });
        Assert.Equal(1, datedRowCount);

        // The REAL resolver (not the Null test double) reads the dated table.
        var lookup = new PayrollContextLookup(
            new PayrollContextRepository(_connectionFactory, new NullAuditService()),
            _connectionFactory);

        var cfg = await lookup.ResolveOtConfigAsync(CorpBwContextId, new DateOnly(2026, 6, 1));

        // Resolver output == the context scalars (the open-ended backfilled row) → parity.
        Assert.Equal(scalar!.OtWeeklyThresholdHours, cfg.WeeklyThresholdHours);
        Assert.Equal(scalar.WorkweekStartDay,        cfg.WorkweekStartDay);
        Assert.Equal(scalar.OtReviewThresholdHours,  cfg.ReviewThresholdHours);
    }

    private sealed record OtScalarRow(
        decimal OtWeeklyThresholdHours, int WorkweekStartDay, decimal? OtReviewThresholdHours);

    /// <summary>
    /// Phase 12.13.2 positive proof (ADR-024). A mid-period policy change must segment the period
    /// by FLSA workweek: weeks before the change keep the old threshold, weeks on/after it get the
    /// new one — and the anchor is fixed as-of period start (D7). Inserts its own two dated rows for
    /// a throwaway context, exercises the REAL <see cref="PayrollContextLookup.ResolveOtConfigForPeriodAsync"/>,
    /// feeds the per-week map through the Core <see cref="OvertimeSplitCalculator"/>, then cleans up.
    /// </summary>
    [Fact]
    public async Task ResolveOtConfigForPeriod_MidPeriodChange_SegmentsByWorkweek()
    {
        // Throwaway context — no payroll_context row needed; the resolver reads only the config table.
        var contextId = Guid.NewGuid();
        var stamp     = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Two Monday-anchored FLSA weeks: Jan 5–11 and Jan 12–18, 2026.
        var periodStart = new DateOnly(2026, 1, 5);
        var periodEnd   = new DateOnly(2026, 1, 18);
        var week1Start  = new DateOnly(2026, 1, 5);
        var week2Start  = new DateOnly(2026, 1, 12);

        using (var conn = _connectionFactory.CreateConnection())
        {
            // Open-ended baseline @ 40h, then a change to 35h effective the SECOND workweek's Monday.
            await InsertConfigRowAsync(conn, contextId, new DateOnly(1, 1, 1), null, 40m, 1, stamp);
            await InsertConfigRowAsync(conn, contextId, week2Start,            null, 35m, 1, stamp);
        }

        try
        {
            var lookup = new PayrollContextLookup(
                new PayrollContextRepository(_connectionFactory, new NullAuditService()),
                _connectionFactory);

            var periodOt = await lookup.ResolveOtConfigForPeriodAsync(contextId, periodStart, periodEnd);

            // Anchor fixed at period start; per-week map carries each week's own threshold.
            Assert.Equal(1, periodOt.WorkweekStartDay);
            Assert.Equal(40m, periodOt.FallbackThresholdHours);                       // as-of period start
            Assert.Equal(40m, periodOt.WeeklyThresholdByWeekStart[week1Start]);       // before the change
            Assert.Equal(35m, periodOt.WeeklyThresholdByWeekStart[week2Start]);       // on/after the change

            // End-to-end through the calculator: 45h each week → week1 5 OT (45-40), week2 10 OT (45-35).
            var daily = new List<(DateOnly, decimal)>();
            for (int d = 0; d < 5; d++) daily.Add((week1Start.AddDays(d), 9m));       // Mon–Fri, 45h
            for (int d = 0; d < 5; d++) daily.Add((week2Start.AddDays(d), 9m));       // Mon–Fri, 45h

            var split = OvertimeSplitCalculator.Compute(
                daily, periodOt.WeeklyThresholdByWeekStart, periodOt.FallbackThresholdHours, periodOt.WorkweekStartDay);

            Assert.Equal(75m, split.RegularHours);   // 40 + 35
            Assert.Equal(15m, split.OvertimeHours);  // 5 + 10 (a uniform-40 period would give only 10)
        }
        finally
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(
                "DELETE FROM payroll_context_ot_config WHERE payroll_context_id = @Id",
                new { Id = contextId });
        }
    }

    static Task InsertConfigRowAsync(
        System.Data.IDbConnection conn, Guid contextId, DateOnly effective, DateOnly? end,
        decimal weeklyThreshold, int workweekStartDay, DateTime stamp)
        => conn.ExecuteAsync(
            """
            INSERT INTO payroll_context_ot_config (
                payroll_context_ot_config_id, payroll_context_id, effective_date, end_date,
                ot_weekly_threshold_hours, workweek_start_day, ot_review_threshold_hours,
                created_by, creation_timestamp)
            VALUES (@Id, @ContextId, @Effective, @End, @Weekly, @StartDay, NULL, @CreatedBy, @Stamp)
            """,
            new
            {
                Id        = Guid.NewGuid(),
                ContextId = contextId,
                Effective = effective,
                End       = end,
                Weekly    = weeklyThreshold,
                StartDay  = workweekStartDay,
                CreatedBy = Guid.Empty,
                Stamp     = stamp
            });
}
