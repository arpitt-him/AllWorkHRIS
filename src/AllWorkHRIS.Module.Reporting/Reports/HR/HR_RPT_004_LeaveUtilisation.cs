using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.HR;

/// <summary>
/// HR-RPT-004 — Leave Utilisation. Per-employee, per-leave-type used vs.
/// entitlement and current balance.
/// </summary>
public sealed class HR_RPT_004_LeaveUtilisation : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public HR_RPT_004_LeaveUtilisation(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "HR-RPT-004";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "HR-RPT-004",
        Title:        "Leave Utilisation",
        ShortName:    "Leave Utilization",
        Description:  "Per-employee leave used vs. entitlement and current balance.",
        AllowedRoles: new[] { "HrisAdmin", "Manager", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    false,
        IsWideReport:  false,
        ParameterShape: ReportParameterShape.AsOfDate);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        const string sql = """
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name) AS employee_name,
                e.employee_number                                       AS employee_number,
                lt.label                                                AS leave_type,
                lb.used_balance                                         AS used,
                lb.entitlement_total                                    AS entitled,
                lb.available_balance                                    AS balance
            FROM   leave_balance lb
            JOIN   employment e   ON e.employment_id = lb.employment_id
            JOIN   person     per ON per.person_id   = e.person_id
            JOIN   lkp_leave_type lt ON lt.id = lb.leave_type_id
            WHERE  (@LegalEntityId IS NULL OR e.legal_entity_id        = @LegalEntityId)
              AND  (@DepartmentId  IS NULL OR e.primary_department_id  = @DepartmentId)
            ORDER BY per.legal_last_name, per.legal_first_name, lt.label
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new { p.LegalEntityId, p.DepartmentId });
        return ReportData.From(rows, ColumnDefs);
    }

    public Task<string> GetParameterSummaryAsync(ReportParameters p, CancellationToken ct)
        => Task.FromResult(p.PeriodStart.HasValue && p.PeriodEnd.HasValue
            ? $"Period: {p.PeriodStart:yyyy-MM-dd} – {p.PeriodEnd:yyyy-MM-dd}"
            : string.Empty);

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("employee_name",   "Employee",   null,     null, "Left"),
        new ReportColumn("employee_number", "EE #",       null,     null, "Left"),
        new ReportColumn("leave_type",      "Leave Type", null,     null, "Left"),
        new ReportColumn("used",            "Used",       "number", "N2", "Right"),
        new ReportColumn("entitled",        "Entitled",   "number", "N2", "Right"),
        new ReportColumn("balance",         "Balance",    "number", "N2", "Right"),
    };
}
