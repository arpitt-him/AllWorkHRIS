using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.Payroll;

/// <summary>
/// PAY-RPT-003 — Employer Cost Report. Employee cost + employer taxes +
/// employer contributions, per employee, for a selected run.
/// </summary>
public sealed class PAY_RPT_003_EmployerCost : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PAY_RPT_003_EmployerCost(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "PAY-RPT-003";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "PAY-RPT-003",
        Title:        "Employer Cost Report",
        ShortName:    "Employer Cost",
        Description:  "Per-employee total employer cost — gross pay plus employer taxes plus employer contributions.",
        AllowedRoles: new[] { "Finance", "PayrollAdmin", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:   true,
        IsWideReport: true);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        if (!p.RunId.HasValue)
            throw new ArgumentException("PAY-RPT-003 requires RunId.", nameof(p));

        const string sql = """
            SELECT
                CONCAT(p.legal_last_name, ', ', p.legal_first_name)             AS employee_name,
                e.employee_number                                               AS employee_number,
                epr.gross_pay_amount                                            AS gross_pay,
                COALESCE(SUM(CASE WHEN trl.employer_flag THEN trl.calculated_amount ELSE 0 END), 0) AS employer_taxes,
                epr.total_employer_contribution_amount                          AS employer_contributions,
                epr.gross_pay_amount
                  + COALESCE(SUM(CASE WHEN trl.employer_flag THEN trl.calculated_amount ELSE 0 END), 0)
                  + epr.total_employer_contribution_amount                      AS total_employer_cost
            FROM   employee_payroll_result epr
            JOIN   employment e ON e.employment_id = epr.employment_id
            JOIN   person     p ON p.person_id     = e.person_id
            LEFT   JOIN tax_result_line trl ON trl.employee_payroll_result_id = epr.employee_payroll_result_id
            WHERE  epr.payroll_run_id = @RunId
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
            GROUP BY p.legal_last_name, p.legal_first_name, e.employee_number,
                     epr.gross_pay_amount, epr.total_employer_contribution_amount
            ORDER BY p.legal_last_name, p.legal_first_name
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new { p.RunId, p.LegalEntityId });
        return ReportData.From(rows, ColumnDefs);
    }

    public async Task<string> GetParameterSummaryAsync(ReportParameters p, CancellationToken ct)
    {
        if (!p.RunId.HasValue) return string.Empty;
        const string sql = """
            SELECT pr.pay_date AS PayDate, pc.payroll_context_name AS ContextName
            FROM   payroll_run pr
            JOIN   payroll_context pc ON pc.payroll_context_id = pr.payroll_context_id
            WHERE  pr.run_id = @RunId
            """;
        using var conn = _connectionFactory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<(DateOnly PayDate, string ContextName)>(sql, new { p.RunId });
        return row.ContextName is null ? string.Empty : $"Run: {row.PayDate:yyyy-MM-dd} — {row.ContextName}";
    }

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("employee_name",          "Employee",                null,       null, "Left"),
        new ReportColumn("employee_number",        "EE #",                    null,       null, "Left"),
        new ReportColumn("gross_pay",              "Gross Pay",               "currency", "C2", "Right"),
        new ReportColumn("employer_taxes",         "Employer Taxes",          "currency", "C2", "Right"),
        new ReportColumn("employer_contributions", "Employer Contributions",  "currency", "C2", "Right"),
        new ReportColumn("total_employer_cost",    "Total Cost",              "currency", "C2", "Right"),
    };
}
