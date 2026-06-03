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

    /// <summary>
    /// Phase 12.13.3 write path (ADR-024). An OT-config edit writes a NEW effective-dated row,
    /// snapping the requested date forward to a workweek boundary (D7), closing the row it lands in,
    /// and leaving the resolver to segment a spanning period. Inserts its own baseline + cleans up.
    /// </summary>
    [Fact]
    public async Task SaveDatedOtConfig_SnapsToWorkweek_ClosesPriorRow_AndResolverSegments()
    {
        var contextId = Guid.NewGuid();
        var stamp     = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using (var conn = _connectionFactory.CreateConnection())
        {
            // Open-ended baseline @ 40h, Monday anchor — like the migration-048 backfill row.
            await InsertConfigRowAsync(conn, contextId, new DateOnly(1, 1, 1), null, 40m, 1, stamp);
        }

        try
        {
            var repo = new PayrollContextRepository(_connectionFactory, new NullAuditService());

            // Request 35h effective a WEDNESDAY (2026-01-07); operativeToday is before it (future change).
            var effective = await repo.SaveDatedOtConfigAsync(
                contextId, 35m, 1, null, new DateOnly(2026, 1, 7), new DateOnly(2026, 1, 1), Guid.Empty);

            // D7: snapped forward to the next Monday.
            Assert.Equal(new DateOnly(2026, 1, 12), effective);

            using var conn = _connectionFactory.CreateConnection();
            var rows = (await conn.QueryAsync<DatedRow>(
                """
                SELECT effective_date AS Effective, end_date AS EndDate, ot_weekly_threshold_hours AS Threshold
                FROM   payroll_context_ot_config WHERE payroll_context_id = @Id ORDER BY effective_date
                """,
                new { Id = contextId })).ToList();

            // Two non-overlapping intervals: prior closed at effective-1, new row open-ended @ 35.
            Assert.Equal(2, rows.Count);
            Assert.Equal(new DateOnly(1, 1, 1),    rows[0].Effective);
            Assert.Equal(new DateOnly(2026, 1, 11), rows[0].EndDate);
            Assert.Equal(40m,                       rows[0].Threshold);
            Assert.Equal(new DateOnly(2026, 1, 12), rows[1].Effective);
            Assert.Null(rows[1].EndDate);
            Assert.Equal(35m,                       rows[1].Threshold);

            // The shared resolver segments a period spanning the boundary.
            var lookup = new PayrollContextLookup(repo, _connectionFactory);
            var periodOt = await lookup.ResolveOtConfigForPeriodAsync(
                contextId, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 18));
            Assert.Equal(40m, periodOt.WeeklyThresholdByWeekStart[new DateOnly(2026, 1, 5)]);
            Assert.Equal(35m, periodOt.WeeklyThresholdByWeekStart[new DateOnly(2026, 1, 12)]);
        }
        finally
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(
                "DELETE FROM payroll_context_ot_config WHERE payroll_context_id = @Id", new { Id = contextId });
        }
    }

    /// <summary>
    /// Phase 12.13.3: re-editing the same workweek updates the existing dated row in place rather
    /// than creating a duplicate interval.
    /// </summary>
    [Fact]
    public async Task SaveDatedOtConfig_SameEffectiveDate_UpdatesInPlace_NoDuplicate()
    {
        var contextId = Guid.NewGuid();
        var stamp     = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var monday    = new DateOnly(2026, 1, 12);

        using (var conn = _connectionFactory.CreateConnection())
            await InsertConfigRowAsync(conn, contextId, new DateOnly(1, 1, 1), null, 40m, 1, stamp);

        try
        {
            var repo = new PayrollContextRepository(_connectionFactory, new NullAuditService());
            await repo.SaveDatedOtConfigAsync(contextId, 35m, 1, null,  monday, new DateOnly(2026, 1, 1), Guid.Empty);
            await repo.SaveDatedOtConfigAsync(contextId, 30m, 1, 50m,   monday, new DateOnly(2026, 1, 1), Guid.Empty);

            using var conn = _connectionFactory.CreateConnection();
            var sameDateCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM payroll_context_ot_config WHERE payroll_context_id = @Id AND effective_date = @E",
                new { Id = contextId, E = monday });
            Assert.Equal(1, sameDateCount);   // re-edited, not duplicated

            var row = await conn.QueryFirstOrDefaultAsync<DatedRow>(
                """
                SELECT effective_date AS Effective, end_date AS EndDate, ot_weekly_threshold_hours AS Threshold
                FROM   payroll_context_ot_config WHERE payroll_context_id = @Id AND effective_date = @E
                """,
                new { Id = contextId, E = monday });
            Assert.Equal(30m, row!.Threshold);   // latest save's values won
        }
        finally
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(
                "DELETE FROM payroll_context_ot_config WHERE payroll_context_id = @Id", new { Id = contextId });
        }
    }

    private sealed record DatedRow(DateOnly Effective, DateOnly? EndDate, decimal Threshold);

    /// <summary>
    /// Phase 12.13.4 (ADR-024): the OT-eligible category-set resolver reads its effective-dated rows
    /// and returns empty when there are none (= "no override", callers fall back to is_worked_time).
    /// A CBA-style override that adds HOLIDAY is resolved as-of a covering date but not before its
    /// effective date. Inserts its own rows for a throwaway context, cleans up. Requires migration 049.
    /// </summary>
    [Fact]
    public async Task ResolveOtEligibleCategories_ReadsDatedSet_EmptyWhenNoneOrBeforeEffective()
    {
        var contextId = Guid.NewGuid();
        var stamp     = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using var conn = _connectionFactory.CreateConnection();
        var cats = (await conn.QueryAsync<CatIdRow>(
            "SELECT id, code FROM lkp_time_category WHERE code IN ('REGULAR','OVERTIME','HOLIDAY')"))
            .ToDictionary(r => r.Code, r => r.Id);
        Assert.True(cats.ContainsKey("REGULAR") && cats.ContainsKey("OVERTIME") && cats.ContainsKey("HOLIDAY"));

        var lookup = new PayrollContextLookup(
            new PayrollContextRepository(_connectionFactory, new NullAuditService()), _connectionFactory);

        // No rows yet → empty (no override).
        Assert.Empty(await lookup.ResolveOtEligibleCategoriesAsync(contextId, new DateOnly(2026, 1, 15)));

        try
        {
            // CBA-style override effective 2026-01-01: REGULAR + OVERTIME + HOLIDAY all count toward OT.
            foreach (var code in new[] { "REGULAR", "OVERTIME", "HOLIDAY" })
                await InsertEligibleRowAsync(conn, contextId, new DateOnly(2026, 1, 1), null, cats[code], stamp);

            var resolved = await lookup.ResolveOtEligibleCategoriesAsync(contextId, new DateOnly(2026, 1, 15));
            Assert.Equal(3, resolved.Count);
            Assert.Contains(cats["HOLIDAY"], resolved);   // the CBA addition is now OT-eligible

            // Before the effective date → not yet in force (resolver returns empty → flag fallback).
            Assert.Empty(await lookup.ResolveOtEligibleCategoriesAsync(contextId, new DateOnly(2025, 12, 31)));
        }
        finally
        {
            await conn.ExecuteAsync(
                "DELETE FROM payroll_context_ot_eligible_category WHERE payroll_context_id = @Id", new { Id = contextId });
        }
    }

    private sealed record CatIdRow(int Id, string Code);

    static Task InsertEligibleRowAsync(
        System.Data.IDbConnection conn, Guid contextId, DateOnly effective, DateOnly? end,
        int timeCategoryId, DateTime stamp)
        => conn.ExecuteAsync(
            """
            INSERT INTO payroll_context_ot_eligible_category (
                payroll_context_ot_eligible_category_id, payroll_context_id, effective_date, end_date,
                time_category_id, created_by, creation_timestamp)
            VALUES (@Id, @ContextId, @Effective, @End, @CatId, @CreatedBy, @Stamp)
            """,
            new
            {
                Id        = Guid.NewGuid(),
                ContextId = contextId,
                Effective = effective,
                End       = end,
                CatId     = timeCategoryId,
                CreatedBy = Guid.Empty,
                Stamp     = stamp
            });

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
