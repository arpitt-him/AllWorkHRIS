using Dapper;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Domain.Time;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class PayrollContextLookup : IPayrollContextLookup
{
    private readonly IPayrollContextRepository _repo;
    private readonly IConnectionFactory        _connectionFactory;

    public PayrollContextLookup(IPayrollContextRepository repo, IConnectionFactory connectionFactory)
    {
        _repo              = repo;
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsAsync()
    {
        var contexts = await _repo.GetAllActiveAsync();
        return contexts.Select(c => (c.PayrollContextId, c.PayrollContextName)).ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsByLegalEntityAsync(Guid legalEntityId)
    {
        var contexts = await _repo.GetByLegalEntityAsync(legalEntityId);
        return contexts.Where(c => c.ContextStatus == "ACTIVE")
                       .Select(c => (c.PayrollContextId, c.PayrollContextName))
                       .ToList();
    }

    // ADR-024 / Phase 12.13: the shared, effective-dated OT-config resolver — one source for
    // both pay (engine) and display (T&A). Returns the dated row whose window covers asOf
    // (most-recent-effective wins, via ORDER BY + first row); system default if none.
    public async Task<OtConfig> ResolveOtConfigAsync(Guid payrollContextId, DateOnly asOf)
    {
        using var conn = _connectionFactory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<OtConfigRow>(
            """
            SELECT ot_weekly_threshold_hours, workweek_start_day, ot_review_threshold_hours
            FROM   payroll_context_ot_config
            WHERE  payroll_context_id = @ContextId
              AND  effective_date <= @AsOf
              AND  (end_date IS NULL OR end_date >= @AsOf)
            ORDER  BY effective_date DESC
            """,
            new { ContextId = payrollContextId, AsOf = asOf.ToDateTime(TimeOnly.MinValue) });

        return row is null
            ? new OtConfig(40m, 1, null)
            : new OtConfig(row.OtWeeklyThresholdHours, row.WorkweekStartDay, row.OtReviewThresholdHours);
    }

    // ADR-024 / Phase 12.13.2: resolve the whole period's OT config — anchor (resolved at period
    // start, D7) + a per-FLSA-week threshold map. One backfilled row ⇒ every week the same value
    // (parity); a mid-period dated change ⇒ weeks before/after get their respective thresholds.
    public async Task<PeriodOtConfig> ResolveOtConfigForPeriodAsync(
        Guid payrollContextId, DateOnly periodStart, DateOnly periodEnd)
    {
        var atStart = await ResolveOtConfigAsync(payrollContextId, periodStart);
        var anchor  = atStart.WorkweekStartDay;

        var byWeek = new Dictionary<DateOnly, decimal>();
        for (var ws = OvertimeSplitCalculator.GetWeekStart(periodStart, anchor); ws <= periodEnd; ws = ws.AddDays(7))
        {
            var cfg = await ResolveOtConfigAsync(payrollContextId, ws);
            byWeek[ws] = cfg.WeeklyThresholdHours;
        }

        return new PeriodOtConfig(anchor, byWeek, atStart.WeeklyThresholdHours, atStart.ReviewThresholdHours);
    }

    // ADR-024 / Phase 12.13.4: the OT-eligible time-category set as-of a date — every dated row
    // covering asOf. Empty (no rows) ⇒ "no override", callers fall back to is_worked_time.
    public async Task<IReadOnlyCollection<int>> ResolveOtEligibleCategoriesAsync(Guid payrollContextId, DateOnly asOf)
    {
        using var conn = _connectionFactory.CreateConnection();
        var ids = await conn.QueryAsync<int>(
            """
            SELECT time_category_id
            FROM   payroll_context_ot_eligible_category
            WHERE  payroll_context_id = @ContextId
              AND  effective_date <= @AsOf
              AND  (end_date IS NULL OR end_date >= @AsOf)
            """,
            new { ContextId = payrollContextId, AsOf = asOf.ToDateTime(TimeOnly.MinValue) });
        return ids.ToList();
    }

    private sealed record OtConfigRow(decimal OtWeeklyThresholdHours, int WorkweekStartDay, decimal? OtReviewThresholdHours);
}
