using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.HR;

/// <summary>
/// HR-RPT-005 — Open Position Vacancy. Positions whose status is OPEN as of
/// the supplied date, with the budgeted headcount and effective-start gap
/// (a rough "days vacant" approximation).
/// </summary>
public sealed class HR_RPT_005_OpenPosition : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public HR_RPT_005_OpenPosition(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "HR-RPT-005";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "HR-RPT-005",
        Title:        "Open Position Vacancy",
        ShortName:    "Open Positions",
        Description:  "Positions with OPEN status, with budgeted headcount and days vacant.",
        AllowedRoles: new[] { "HrisAdmin", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    false,
        IsWideReport:  false,
        ParameterShape: ReportParameterShape.AsOfDate);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        const string sql = """
            SELECT
                COALESCE(pos.position_title, j.job_title)              AS title,
                j.job_code                                             AS job_code,
                COALESCE(ou.org_unit_name, 'Unassigned')               AS department,
                pos.headcount_budget                                   AS budget,
                pos.effective_start_date                               AS opened_on,
                CAST(@AsOf AS date) - pos.effective_start_date         AS days_vacant
            FROM   position pos
            JOIN   lkp_position_status ps ON ps.id = pos.position_status_id
            LEFT   JOIN job j   ON j.job_id     = pos.job_id
            LEFT   JOIN org_unit ou ON ou.org_unit_id = pos.org_unit_id
            WHERE  ps.code = 'OPEN'
              AND  pos.effective_start_date <= @AsOf
              AND  (pos.effective_end_date IS NULL OR pos.effective_end_date >= @AsOf)
              AND  (@DepartmentId IS NULL OR pos.org_unit_id = @DepartmentId)
            ORDER BY days_vacant DESC, title
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            AsOf = asOf.ToDateTime(TimeOnly.MinValue),
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
        new ReportColumn("title",       "Title",       null,     null,  "Left"),
        new ReportColumn("job_code",    "Job Code",    null,     null,  "Left"),
        new ReportColumn("department",  "Department",  null,     null,  "Left"),
        new ReportColumn("budget",      "Budget HC",   "number", "N0",  "Right"),
        new ReportColumn("opened_on",   "Opened",      "date",   "yMd", "Left"),
        new ReportColumn("days_vacant", "Days Vacant", "number", "N0",  "Right"),
    };
}
