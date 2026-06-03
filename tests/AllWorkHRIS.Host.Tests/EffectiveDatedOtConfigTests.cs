using Dapper;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Data;
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
}
