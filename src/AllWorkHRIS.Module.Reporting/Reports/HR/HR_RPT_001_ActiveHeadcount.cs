using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.HR;

/// <summary>
/// HR-RPT-001 — Active Headcount. By department: total headcount,
/// full-time / part-time split, and exempt / non-exempt split as of a
/// selected date (defaults to operative date).
/// </summary>
public sealed class HR_RPT_001_ActiveHeadcount : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public HR_RPT_001_ActiveHeadcount(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "HR-RPT-001";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "HR-RPT-001",
        Title:        "Active Headcount",
        ShortName:    "Headcount",
        Description:  "Active headcount by department, with full-time / part-time and exempt / non-exempt splits.",
        AllowedRoles: new[] { "HrisAdmin", "HrisViewer", "Manager", "Finance", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    true,
        IsWideReport:  false,
        ParameterShape: ReportParameterShape.AsOfDate);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        const string sql = """
            SELECT
                COALESCE(ou.org_unit_name, 'Unassigned')                       AS department,
                COUNT(*)                                                       AS headcount,
                SUM(CASE WHEN fpt.code = 'FULL_TIME' THEN 1 ELSE 0 END)        AS full_time,
                SUM(CASE WHEN fpt.code = 'PART_TIME' THEN 1 ELSE 0 END)        AS part_time,
                SUM(CASE WHEN flsa.overtime_eligible = false THEN 1 ELSE 0 END) AS exempt,
                SUM(CASE WHEN flsa.overtime_eligible = true  THEN 1 ELSE 0 END) AS non_exempt
            FROM   employment e
            JOIN   lkp_employment_status        es   ON es.id   = e.employment_status_id
            JOIN   lkp_full_part_time_status    fpt  ON fpt.id  = e.full_part_time_status_id
            JOIN   lkp_flsa_status              flsa ON flsa.id = e.flsa_status_id
            LEFT   JOIN org_unit ou                ON ou.org_unit_id = e.primary_department_id
            WHERE  es.code = 'ACTIVE'
              AND  e.employment_start_date <= @AsOf
              AND  (e.employment_end_date IS NULL OR e.employment_end_date >= @AsOf)
              AND  (@LegalEntityId  IS NULL OR e.legal_entity_id      = @LegalEntityId)
              AND  (@DepartmentId   IS NULL OR e.primary_department_id = @DepartmentId)
            GROUP BY ou.org_unit_id, ou.org_unit_name
            ORDER BY ou.org_unit_name
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            AsOf = asOf.ToDateTime(TimeOnly.MinValue),
            p.LegalEntityId,
            p.DepartmentId
        });
        return ReportData.From(rows, ColumnDefs);
    }

    public Task<string> GetParameterSummaryAsync(ReportParameters p, CancellationToken ct)
    {
        var asOf = p.AsOfDate?.ToString("yyyy-MM-dd") ?? "current";
        return Task.FromResult($"As of: {asOf}");
    }

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("department",  "Department", null,     null, "Left"),
        new ReportColumn("headcount",   "Headcount",  "number", "N0", "Right"),
        new ReportColumn("full_time",   "Full Time",  "number", "N0", "Right"),
        new ReportColumn("part_time",   "Part Time",  "number", "N0", "Right"),
        new ReportColumn("exempt",      "Exempt",     "number", "N0", "Right"),
        new ReportColumn("non_exempt",  "Non-Exempt", "number", "N0", "Right"),
    };
}
