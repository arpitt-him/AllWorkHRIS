using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.HR;

/// <summary>
/// HR-RPT-007 — Onboarding Status. Active onboarding plans with status,
/// target start date, and the count of outstanding blocking tasks.
/// </summary>
public sealed class HR_RPT_007_OnboardingStatus : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public HR_RPT_007_OnboardingStatus(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "HR-RPT-007";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "HR-RPT-007",
        Title:        "Onboarding Status",
        ShortName:    "Onboarding",
        Description:  "Active onboarding plans with status and outstanding blocking-task count.",
        AllowedRoles: new[] { "HrisAdmin", "Manager", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    false,
        IsWideReport:  false,
        ParameterShape: ReportParameterShape.Period);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        const string sql = """
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name)  AS employee_name,
                e.employee_number                                        AS employee_number,
                op.target_start_date                                     AS target_start,
                ps.label                                                 AS plan_status,
                (SELECT COUNT(*) FROM onboarding_task t
                  JOIN lkp_onboarding_task_status ts ON ts.id = t.task_status_id
                  WHERE t.onboarding_plan_id = op.onboarding_plan_id
                    AND t.blocking_flag = true
                    AND ts.code NOT IN ('COMPLETED','WAIVED'))            AS open_blocking_tasks
            FROM   onboarding_plan op
            JOIN   employment e   ON e.employment_id = op.employment_id
            JOIN   person     per ON per.person_id   = e.person_id
            JOIN   lkp_onboarding_plan_status ps ON ps.id = op.plan_status_id
            WHERE  (@LegalEntityId IS NULL OR e.legal_entity_id        = @LegalEntityId)
              AND  (@DepartmentId  IS NULL OR e.primary_department_id  = @DepartmentId)
              AND  (@PeriodStart IS NULL OR op.target_start_date >= @PeriodStart)
              AND  (@PeriodEnd   IS NULL OR op.target_start_date <= @PeriodEnd)
            ORDER BY op.target_start_date DESC, per.legal_last_name
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            p.LegalEntityId,
            p.DepartmentId,
            PeriodStart = (object?)p.PeriodStart?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value,
            PeriodEnd   = (object?)p.PeriodEnd?.ToDateTime(TimeOnly.MinValue)   ?? DBNull.Value
        });
        return ReportData.From(rows, ColumnDefs);
    }

    public Task<string> GetParameterSummaryAsync(ReportParameters p, CancellationToken ct)
        => Task.FromResult(p.PeriodStart.HasValue && p.PeriodEnd.HasValue
            ? $"Period: {p.PeriodStart:yyyy-MM-dd} – {p.PeriodEnd:yyyy-MM-dd}"
            : string.Empty);

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("employee_name",        "Employee",            null,     null,  "Left"),
        new ReportColumn("employee_number",      "EE #",                null,     null,  "Left"),
        new ReportColumn("target_start",         "Target Start",        "date",   "yMd", "Left"),
        new ReportColumn("plan_status",          "Plan Status",         null,     null,  "Left"),
        new ReportColumn("open_blocking_tasks",  "Open Blocking Tasks", "number", "N0",  "Right"),
    };
}
