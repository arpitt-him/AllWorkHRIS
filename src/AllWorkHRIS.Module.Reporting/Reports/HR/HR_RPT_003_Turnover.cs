using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.HR;

/// <summary>
/// HR-RPT-003 — Turnover Report. Terminations per department over the
/// supplied period, plus a simple turnover rate against the headcount at
/// the start of the period.
/// </summary>
public sealed class HR_RPT_003_Turnover : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public HR_RPT_003_Turnover(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "HR-RPT-003";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "HR-RPT-003",
        Title:        "Turnover Report",
        ShortName:    "Turnover",
        Description:  "Terminations and turnover rate by department over the selected period.",
        AllowedRoles: new[] { "HrisAdmin", "Finance", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    true,
        IsWideReport:  false,
        ParameterShape: ReportParameterShape.Period);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        if (!p.PeriodStart.HasValue || !p.PeriodEnd.HasValue)
            throw new ArgumentException("HR-RPT-003 requires PeriodStart and PeriodEnd.", nameof(p));

        const string sql = """
            WITH dept AS (
                SELECT ou.org_unit_id, COALESCE(ou.org_unit_name, 'Unassigned') AS department
                FROM   org_unit ou
            ),
            head_start AS (
                SELECT e.primary_department_id AS dept_id, COUNT(*) AS hc
                FROM   employment e
                JOIN   lkp_employment_status es ON es.id = e.employment_status_id
                WHERE  e.employment_start_date <= @PeriodStart
                  AND  (e.employment_end_date IS NULL OR e.employment_end_date >= @PeriodStart)
                  AND  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
                GROUP BY e.primary_department_id
            ),
            terms AS (
                SELECT e.primary_department_id AS dept_id, COUNT(*) AS total_terms
                FROM   employment e
                WHERE  e.termination_date BETWEEN @PeriodStart AND @PeriodEnd
                  AND  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
                GROUP BY e.primary_department_id
            )
            SELECT
                dept.department                                                AS department,
                COALESCE(head_start.hc, 0)                                     AS start_headcount,
                COALESCE(terms.total_terms, 0)                                 AS terminations,
                CASE WHEN COALESCE(head_start.hc, 0) = 0 THEN NULL
                     ELSE COALESCE(terms.total_terms, 0) * 100.0 / head_start.hc
                END                                                            AS turnover_pct
            FROM   dept
            LEFT   JOIN head_start ON head_start.dept_id = dept.org_unit_id
            LEFT   JOIN terms      ON terms.dept_id      = dept.org_unit_id
            WHERE  COALESCE(head_start.hc, 0) > 0 OR COALESCE(terms.total_terms, 0) > 0
              AND  (@DepartmentId IS NULL OR dept.org_unit_id = @DepartmentId)
            ORDER BY dept.department
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            PeriodStart = p.PeriodStart.Value.ToDateTime(TimeOnly.MinValue),
            PeriodEnd   = p.PeriodEnd.Value.ToDateTime(TimeOnly.MinValue),
            p.LegalEntityId,
            p.DepartmentId
        });
        return ReportData.From(rows, ColumnDefs);
    }

    public Task<string> GetParameterSummaryAsync(ReportParameters p, CancellationToken ct)
        => Task.FromResult(p.PeriodStart.HasValue && p.PeriodEnd.HasValue
            ? $"Period: {p.PeriodStart:yyyy-MM-dd} – {p.PeriodEnd:yyyy-MM-dd}"
            : string.Empty);

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("department",      "Department",     null,     null, "Left"),
        new ReportColumn("start_headcount", "Start HC",       "number", "N0", "Right"),
        new ReportColumn("terminations",    "Terminations",   "number", "N0", "Right"),
        new ReportColumn("turnover_pct",    "Turnover %",     "number", "N2", "Right"),
    };
}
