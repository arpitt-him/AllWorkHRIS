using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.HR;

/// <summary>
/// HR-RPT-006 — Compensation Summary. Per-employee primary compensation
/// effective as of a selected date — pay type, rate type, base rate, and
/// annual equivalent.
/// </summary>
public sealed class HR_RPT_006_CompensationSummary : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public HR_RPT_006_CompensationSummary(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "HR-RPT-006";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "HR-RPT-006",
        Title:        "Compensation Summary",
        ShortName:    "Compensation",
        Description:  "Per-employee compensation effective as of the selected date.",
        AllowedRoles: new[] { "HrisAdmin", "Finance", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    true,
        IsWideReport:  true,
        ParameterShape: ReportParameterShape.AsOfDate);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        const string sql = """
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name) AS employee_name,
                e.employee_number                                       AS employee_number,
                COALESCE(ou.org_unit_name, 'Unassigned')                AS department,
                pt.label                                                AS pay_type,
                rt.label                                                AS rate_type,
                cr.base_rate                                            AS base_rate,
                cr.annual_equivalent                                    AS annual_equivalent
            FROM   compensation_record cr
            JOIN   employment e   ON e.employment_id = cr.employment_id
            JOIN   person     per ON per.person_id   = e.person_id
            LEFT   JOIN lkp_pay_type pt                ON pt.id = cr.pay_type_id
            JOIN   lkp_compensation_rate_type rt       ON rt.id = cr.rate_type_id
            LEFT   JOIN org_unit ou                    ON ou.org_unit_id = e.primary_department_id
            WHERE  cr.primary_rate_flag = true
              AND  cr.effective_start_date <= @AsOf
              AND  (cr.effective_end_date IS NULL OR cr.effective_end_date >= @AsOf)
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id        = @LegalEntityId)
              AND  (@DepartmentId  IS NULL OR e.primary_department_id  = @DepartmentId)
            ORDER BY per.legal_last_name, per.legal_first_name
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
        new ReportColumn("employee_name",     "Employee",          null,       null, "Left"),
        new ReportColumn("employee_number",   "EE #",              null,       null, "Left"),
        new ReportColumn("department",        "Department",        null,       null, "Left"),
        new ReportColumn("pay_type",          "Pay Type",          null,       null, "Left"),
        new ReportColumn("rate_type",         "Rate Type",         null,       null, "Left"),
        new ReportColumn("base_rate",         "Base Rate",         "currency", "C2", "Right"),
        new ReportColumn("annual_equivalent", "Annual Equivalent", "currency", "C2", "Right"),
    };
}
