// AllWorkHRIS.Module.Payroll/PayrollDashboardContributor.cs
using AllWorkHRIS.Core.Dashboard;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Temporal;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll;

public sealed class PayrollDashboardContributor : IDashboardContributor
{
    private readonly IConnectionFactory                  _connectionFactory;
    private readonly ITemporalContext                    _temporal;
    private readonly ILogger<PayrollDashboardContributor> _logger;

    public string                 ModuleName    => "Payroll";
    public string                 AccentColor   => "var(--module-payroll)";
    public IReadOnlyList<string>  RequiredRoles => ["PayrollOperator", "PayrollAdmin"];

    public PayrollDashboardContributor(
        IConnectionFactory                   connectionFactory,
        ITemporalContext                     temporal,
        ILogger<PayrollDashboardContributor> logger)
    {
        _connectionFactory = connectionFactory;
        _temporal          = temporal;
        _logger            = logger;
    }

    public async Task<IReadOnlyList<DashboardItem>> GetItemsAsync(
        Guid?               entityId,
        IEnumerable<string> userRoles,
        CancellationToken   ct = default)
    {
        var items = new List<DashboardItem>();

        try
        {
            using var conn = _connectionFactory.CreateConnection();

            // Runs awaiting approval
            const string pendingSql =
                """
                SELECT
                    pr.run_id,
                    pp.period_start_date,
                    pp.period_end_date,
                    pp.pay_date,
                    ou.org_unit_id   AS entity_id,
                    ou.org_unit_name AS entity_name
                FROM payroll_run pr
                JOIN lkp_run_status  rs ON rs.id = pr.run_status_id
                JOIN payroll_context pc ON pc.payroll_context_id = pr.payroll_context_id
                JOIN payroll_period  pp ON pp.period_id = pr.period_id
                JOIN org_unit        ou ON ou.org_unit_id = pc.legal_entity_id
                WHERE rs.code = 'PENDING_APPROVAL'
                  AND (@EntityId IS NULL OR pc.legal_entity_id = @EntityId)
                ORDER BY pp.pay_date
                LIMIT 10
                """;

            var pending = await conn.QueryAsync(pendingSql, new { EntityId = entityId });

            foreach (var row in pending)
            {
                items.Add(new DashboardItem(
                    Title:       "Payroll run pending approval",
                    Subtitle:    $"Pay date {(DateOnly)row.pay_date:MMM d, yyyy}",
                    EntityId:    (Guid)row.entity_id,
                    EntityName:  (string)row.entity_name,
                    Route:       $"/payroll/runs/{(Guid)row.run_id}",
                    Urgency:     DashboardItemUrgency.Urgent,
                    ModuleName:  ModuleName,
                    AccentColor: AccentColor));
            }

            // Upcoming pay dates without a run (next 60 days)
            var today = DateOnly.FromDateTime(_temporal.GetOperativeDate());
            var sql = entityId.HasValue
                ? """
                  SELECT
                      pp.period_id,
                      pp.pay_date,
                      ou.org_unit_id   AS entity_id,
                      ou.org_unit_name AS entity_name
                  FROM payroll_period pp
                  JOIN payroll_context pc ON pc.payroll_context_id = pp.payroll_context_id
                  JOIN org_unit        ou ON ou.org_unit_id = pc.legal_entity_id
                  WHERE pp.pay_date >= @Today
                    AND pp.pay_date <= @Horizon
                    AND pc.legal_entity_id = @EntityId
                    AND NOT EXISTS (
                        SELECT 1 FROM payroll_run pr2
                        WHERE pr2.period_id = pp.period_id
                    )
                  ORDER BY pp.pay_date
                  LIMIT 3
                  """
                : """
                  SELECT
                      pp.period_id,
                      pp.pay_date,
                      ou.org_unit_id   AS entity_id,
                      ou.org_unit_name AS entity_name
                  FROM payroll_period pp
                  JOIN payroll_context pc ON pc.payroll_context_id = pp.payroll_context_id
                  JOIN org_unit        ou ON ou.org_unit_id = pc.legal_entity_id
                  WHERE pp.pay_date >= @Today
                    AND pp.pay_date <= @Horizon
                    AND NOT EXISTS (
                        SELECT 1 FROM payroll_run pr2
                        WHERE pr2.period_id = pp.period_id
                    )
                  ORDER BY pp.pay_date
                  LIMIT 5
                  """;

            var upcoming = await conn.QueryAsync(sql,
                new
                {
                    Today    = today.ToDateTime(TimeOnly.MinValue),
                    Horizon  = today.AddDays(60).ToDateTime(TimeOnly.MinValue),
                    EntityId = entityId,
                });

            foreach (var row in upcoming)
            {
                items.Add(new DashboardItem(
                    Title:       "Upcoming pay date",
                    Subtitle:    $"{(DateOnly)row.pay_date:MMM d, yyyy} — no run initiated",
                    EntityId:    (Guid)row.entity_id,
                    EntityName:  (string)row.entity_name,
                    Route:       "/payroll/runs",
                    Urgency:     DashboardItemUrgency.Upcoming,
                    ModuleName:  ModuleName,
                    AccentColor: AccentColor));
            }

            // Tax elections incomplete for employees in imminent payroll runs
            var today2 = DateOnly.FromDateTime(_temporal.GetOperativeDate());
            const string notReadySql = """
                WITH imminent_runs AS (
                    SELECT r.payroll_context_id,
                           pc.payroll_context_name,
                           pc.cutoff_offset_days,
                           MIN(r.pay_date)   AS next_pay_date,
                           ou.org_unit_id    AS entity_id,
                           ou.org_unit_name  AS entity_name
                    FROM   payroll_run      r
                    JOIN   lkp_run_status   rs ON rs.id = r.run_status_id
                    JOIN   payroll_context  pc ON pc.payroll_context_id = r.payroll_context_id
                    JOIN   org_unit         ou ON ou.org_unit_id = pc.legal_entity_id
                    WHERE  r.pay_date >= @Today
                      AND  rs.code NOT IN ('RELEASED','CLOSED','CANCELLED','FAILED')
                      AND  (@EntityId IS NULL OR pc.legal_entity_id = @EntityId)
                    GROUP BY r.payroll_context_id, pc.payroll_context_name, pc.cutoff_offset_days,
                             ou.org_unit_id, ou.org_unit_name
                ),
                erj AS (
                    SELECT e.employment_id, lej.jurisdiction_id
                    FROM   employment e
                    JOIN   lkp_employment_status es  ON es.id = e.employment_status_id
                    JOIN   legal_entity_jurisdiction lej ON lej.legal_entity_id = e.legal_entity_id
                    JOIN   tax_jurisdiction j ON j.jurisdiction_id = lej.jurisdiction_id
                    WHERE  es.is_payroll_active = TRUE AND e.payroll_eligibility_flag = TRUE
                      AND  j.jurisdiction_code IN ('US-FED','CA-FED') AND j.is_active = TRUE
                      AND  (@EntityId IS NULL OR e.legal_entity_id = @EntityId)
                    UNION
                    SELECT e.employment_id, lej.jurisdiction_id
                    FROM   employment e
                    JOIN   lkp_employment_status es  ON es.id = e.employment_status_id
                    JOIN   person_address pa ON pa.person_id = e.person_id
                    JOIN   tax_jurisdiction j ON j.jurisdiction_code = pa.country_code || '-' || pa.state_code
                    JOIN   legal_entity_jurisdiction lej ON lej.jurisdiction_id = j.jurisdiction_id
                    WHERE  es.is_payroll_active = TRUE AND e.payroll_eligibility_flag = TRUE
                      AND  pa.address_type = 'PRIMARY'
                      AND  pa.effective_start_date <= @Today
                      AND  (pa.effective_end_date IS NULL OR pa.effective_end_date >= @Today)
                      AND  lej.legal_entity_id = e.legal_entity_id AND j.is_active = TRUE
                      AND  (@EntityId IS NULL OR e.legal_entity_id = @EntityId)
                    UNION
                    SELECT e.employment_id, lej.jurisdiction_id
                    FROM   employment e
                    JOIN   lkp_employment_status es  ON es.id = e.employment_status_id
                    JOIN   org_unit ou ON ou.org_unit_id = e.primary_work_location_id
                    JOIN   tax_jurisdiction j ON j.jurisdiction_code = COALESCE(ou.country_code,'US') || '-' || ou.state_code
                    JOIN   legal_entity_jurisdiction lej ON lej.jurisdiction_id = j.jurisdiction_id
                    WHERE  es.is_payroll_active = TRUE AND e.payroll_eligibility_flag = TRUE
                      AND  ou.state_code IS NOT NULL
                      AND  lej.legal_entity_id = e.legal_entity_id AND j.is_active = TRUE
                      AND  (@EntityId IS NULL OR e.legal_entity_id = @EntityId)
                ),
                missing AS (
                    SELECT DISTINCT erj.employment_id FROM erj
                    WHERE NOT EXISTS (
                        SELECT 1 FROM employee_tax_form_submission s
                        WHERE  s.employment_id   = erj.employment_id
                          AND  s.jurisdiction_id = erj.jurisdiction_id
                          AND  s.effective_from <= @Today
                          AND  (s.effective_to IS NULL OR s.effective_to >= @Today)
                    )
                )
                SELECT ir.payroll_context_name AS ContextName,
                       ir.next_pay_date        AS NextPayDate,
                       ir.cutoff_offset_days   AS CutoffOffsetDays,
                       ir.entity_id            AS EntityId,
                       ir.entity_name          AS EntityName,
                       COUNT(DISTINCT pp.employment_id) AS MissingCount
                FROM   imminent_runs   ir
                JOIN   payroll_profile pp ON pp.payroll_context_id = ir.payroll_context_id
                                         AND pp.enrollment_status = 'ACTIVE'
                JOIN   missing         m  ON m.employment_id = pp.employment_id
                GROUP BY ir.payroll_context_name, ir.next_pay_date, ir.cutoff_offset_days,
                         ir.entity_id, ir.entity_name
                HAVING COUNT(DISTINCT pp.employment_id) > 0
                ORDER BY ir.next_pay_date, ir.payroll_context_name
                LIMIT 10
                """;

            var notReadyRows = await conn.QueryAsync<NotReadyRow>(notReadySql, new
            {
                Today    = today2.ToDateTime(TimeOnly.MinValue),
                EntityId = entityId
            });

            foreach (var row in notReadyRows)
            {
                var cutoffDate = row.NextPayDate.AddDays(-row.CutoffOffsetDays);
                var urgency    = today2 >= cutoffDate
                    ? DashboardItemUrgency.Urgent
                    : DashboardItemUrgency.Attention;

                items.Add(new DashboardItem(
                    Title:       "Tax elections incomplete",
                    Subtitle:    $"{row.MissingCount} employee{(row.MissingCount == 1 ? "" : "s")} not ready · {row.ContextName} · pay {row.NextPayDate:MMM d, yyyy}",
                    EntityId:    row.EntityId,
                    EntityName:  row.EntityName,
                    Route:       "/payroll/tax-profiles",
                    Urgency:     urgency,
                    ModuleName:  ModuleName,
                    AccentColor: AccentColor));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayrollDashboardContributor query failed");
        }

        return items;
    }

    private sealed record NotReadyRow(
        string   ContextName,
        DateOnly NextPayDate,
        int      CutoffOffsetDays,
        Guid     EntityId,
        string   EntityName,
        long     MissingCount);
}
