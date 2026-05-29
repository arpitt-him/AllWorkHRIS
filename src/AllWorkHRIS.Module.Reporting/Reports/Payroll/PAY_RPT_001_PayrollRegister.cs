using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.Payroll;

/// <summary>
/// PAY-RPT-001 — Payroll Register. One row per employee for the selected run,
/// optionally filtered by legal entity and department.
/// </summary>
public sealed class PAY_RPT_001_PayrollRegister : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PAY_RPT_001_PayrollRegister(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "PAY-RPT-001";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "PAY-RPT-001",
        Title:        "Payroll Register Report",
        ShortName:    "Payroll Register",
        Description:  "Employee-by-employee gross, deductions, taxes, and net pay for a selected payroll run.",
        AllowedRoles: new[] { "PayrollOperator", "PayrollAdmin", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:   true,
        IsWideReport: true);

    public async Task<ReportData> ExecuteAsync(
        ReportParameters parameters,
        DateOnly         asOf,
        CancellationToken ct)
    {
        if (!parameters.RunId.HasValue)
            throw new ArgumentException("PAY-RPT-001 requires RunId.", nameof(parameters));

        const string sql = """
            SELECT
                CONCAT(p.legal_last_name, ', ', p.legal_first_name)  AS employee_name,
                e.employee_number                                    AS employee_number,
                COALESCE(ou.org_unit_name, 'Unassigned')             AS department,
                epr.pay_period_start_date                            AS pay_period_start,
                epr.pay_period_end_date                              AS pay_period_end,
                epr.gross_pay_amount                                 AS gross_pay,
                epr.total_deductions_amount                          AS total_deductions,
                epr.total_employee_tax_amount                        AS total_taxes,
                epr.total_employer_contribution_amount               AS er_contributions,
                epr.net_pay_amount                                   AS net_pay,
                rs.label                                             AS result_status
            FROM   employee_payroll_result epr
            JOIN   employment e        ON e.employment_id = epr.employment_id
            JOIN   person     p        ON p.person_id     = e.person_id
            LEFT   JOIN assignment a
                   ON  a.employment_id        = e.employment_id
                   AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                   AND a.assignment_start_date <= @AsOf
                   AND (a.assignment_end_date IS NULL OR a.assignment_end_date >= @AsOf)
            LEFT   JOIN org_unit ou    ON ou.org_unit_id = a.department_id
            JOIN   lkp_employee_result_status rs ON rs.id = epr.result_status_id
            WHERE  epr.payroll_run_id = @RunId
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
              AND  (@DepartmentId  IS NULL OR a.department_id   = @DepartmentId)
            ORDER BY p.legal_last_name, p.legal_first_name
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            parameters.RunId,
            parameters.LegalEntityId,
            parameters.DepartmentId,
            AsOf = asOf.ToDateTime(TimeOnly.MinValue)
        });

        return ReportData.From(rows, ColumnDefs);
    }

    public async Task<string> GetParameterSummaryAsync(ReportParameters p, CancellationToken ct)
    {
        if (!p.RunId.HasValue) return string.Empty;

        const string sql = """
            SELECT pr.pay_date              AS PayDate,
                   pc.payroll_context_name  AS ContextName
            FROM   payroll_run pr
            JOIN   payroll_context pc ON pc.payroll_context_id = pr.payroll_context_id
            WHERE  pr.run_id = @RunId
            """;

        using var conn = _connectionFactory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<RunHeader>(sql, new { p.RunId });
        return row is null
            ? string.Empty
            : $"Run: {row.PayDate:yyyy-MM-dd} — {row.ContextName}";
    }

    private sealed record RunHeader(DateOnly PayDate, string ContextName);

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("employee_name",     "Employee",         null,       null,    "Left"),
        new ReportColumn("employee_number",   "EE #",             null,       null,    "Left"),
        new ReportColumn("department",        "Department",       null,       null,    "Left"),
        new ReportColumn("pay_period_start",  "Period Start",     "date",     "yMd",   "Left"),
        new ReportColumn("pay_period_end",    "Period End",       "date",     "yMd",   "Left"),
        new ReportColumn("gross_pay",         "Gross",            "currency", "C2",    "Right"),
        new ReportColumn("total_deductions",  "Deductions",       "currency", "C2",    "Right"),
        new ReportColumn("total_taxes",       "EE Taxes",         "currency", "C2",    "Right"),
        new ReportColumn("er_contributions",  "ER Contributions", "currency", "C2",    "Right"),
        new ReportColumn("net_pay",           "Net Pay",          "currency", "C2",    "Right"),
        new ReportColumn("result_status",     "Status",           null,       null,    "Left")
    };
}
