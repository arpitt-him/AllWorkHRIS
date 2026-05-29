using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.HR;

/// <summary>
/// HR-RPT-002 — New Hire &amp; Termination. Hires and terminations whose
/// effective date falls in the supplied PeriodStart .. PeriodEnd range.
/// </summary>
public sealed class HR_RPT_002_NewHireTermination : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public HR_RPT_002_NewHireTermination(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "HR-RPT-002";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "HR-RPT-002",
        Title:        "New Hire & Termination",
        ShortName:    "Hires & Terms",
        Description:  "Hires and terminations in the selected period, by department.",
        AllowedRoles: new[] { "HrisAdmin", "Finance", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    false,
        IsWideReport:  true,
        ParameterShape: ReportParameterShape.Period);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        if (!p.PeriodStart.HasValue || !p.PeriodEnd.HasValue)
            throw new ArgumentException("HR-RPT-002 requires PeriodStart and PeriodEnd.", nameof(p));

        const string sql = """
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name)          AS employee_name,
                e.employee_number                                                AS employee_number,
                'HIRE'                                                           AS event_type,
                e.employment_start_date                                          AS effective_date,
                COALESCE(ou.org_unit_name, 'Unassigned')                         AS department,
                NULL                                                             AS reason
            FROM   employment e
            JOIN   person     per ON per.person_id = e.person_id
            LEFT   JOIN org_unit ou ON ou.org_unit_id = e.primary_department_id
            WHERE  e.employment_start_date BETWEEN @PeriodStart AND @PeriodEnd
              AND  (@EventType IS NULL OR @EventType = 'HIRE')
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id        = @LegalEntityId)
              AND  (@DepartmentId  IS NULL OR e.primary_department_id  = @DepartmentId)
            UNION ALL
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name),
                e.employee_number,
                'TERMINATION',
                e.termination_date,
                COALESCE(ou.org_unit_name, 'Unassigned'),
                NULL
            FROM   employment e
            JOIN   person     per ON per.person_id = e.person_id
            LEFT   JOIN org_unit ou ON ou.org_unit_id = e.primary_department_id
            WHERE  e.termination_date IS NOT NULL
              AND  e.termination_date BETWEEN @PeriodStart AND @PeriodEnd
              AND  (@EventType IS NULL OR @EventType = 'TERMINATION')
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id        = @LegalEntityId)
              AND  (@DepartmentId  IS NULL OR e.primary_department_id  = @DepartmentId)
            ORDER BY effective_date, employee_name
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            PeriodStart = p.PeriodStart.Value.ToDateTime(TimeOnly.MinValue),
            PeriodEnd   = p.PeriodEnd.Value.ToDateTime(TimeOnly.MinValue),
            p.EventType,
            p.LegalEntityId,
            p.DepartmentId
        });
        return ReportData.From(rows, ColumnDefs);
    }

    public Task<string> GetParameterSummaryAsync(ReportParameters p, CancellationToken ct)
    {
        if (!p.PeriodStart.HasValue || !p.PeriodEnd.HasValue) return Task.FromResult(string.Empty);
        var et = string.IsNullOrEmpty(p.EventType) ? string.Empty : $" · {p.EventType}";
        return Task.FromResult($"Period: {p.PeriodStart:yyyy-MM-dd} – {p.PeriodEnd:yyyy-MM-dd}{et}");
    }

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("employee_name",   "Employee",       null,   null,  "Left"),
        new ReportColumn("employee_number", "EE #",           null,   null,  "Left"),
        new ReportColumn("event_type",      "Event",          null,   null,  "Left"),
        new ReportColumn("effective_date",  "Effective Date", "date", "yMd", "Left"),
        new ReportColumn("department",      "Department",     null,   null,  "Left"),
        new ReportColumn("reason",          "Reason",         null,   null,  "Left"),
    };
}
