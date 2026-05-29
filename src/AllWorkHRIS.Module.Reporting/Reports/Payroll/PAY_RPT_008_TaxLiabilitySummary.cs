using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.Payroll;

/// <summary>
/// PAY-RPT-008 — Tax Liability Summary. Employee + employer tax totals by
/// jurisdiction and tax code for the selected run.
/// </summary>
public sealed class PAY_RPT_008_TaxLiabilitySummary : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PAY_RPT_008_TaxLiabilitySummary(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "PAY-RPT-008";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "PAY-RPT-008",
        Title:        "Tax Liability Summary",
        ShortName:    "Tax Liability",
        Description:  "Employee and employer tax totals by jurisdiction and tax code for the selected run.",
        AllowedRoles: new[] { "Finance", "TaxAdmin", "PayrollAdmin", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:   true,
        IsWideReport: false);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        if (!p.RunId.HasValue)
            throw new ArgumentException("PAY-RPT-008 requires RunId.", nameof(p));

        const string sql = """
            SELECT
                j.jurisdiction_name                                                AS jurisdiction,
                trl.tax_code                                                       AS tax_code,
                trl.tax_description                                                AS description,
                SUM(CASE WHEN trl.employer_flag = false THEN trl.calculated_amount ELSE 0 END) AS employee_amount,
                SUM(CASE WHEN trl.employer_flag = true  THEN trl.calculated_amount ELSE 0 END) AS employer_amount,
                SUM(trl.calculated_amount)                                         AS total
            FROM   tax_result_line trl
            JOIN   employee_payroll_result epr ON epr.employee_payroll_result_id = trl.employee_payroll_result_id
            JOIN   employment e                ON e.employment_id = epr.employment_id
            JOIN   jurisdiction j              ON j.jurisdiction_id = trl.jurisdiction_id
            WHERE  epr.payroll_run_id = @RunId
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
            GROUP BY j.jurisdiction_name, trl.tax_code, trl.tax_description
            ORDER BY j.jurisdiction_name, trl.tax_code
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
        new ReportColumn("jurisdiction",    "Jurisdiction", null,       null, "Left"),
        new ReportColumn("tax_code",        "Tax Code",     null,       null, "Left"),
        new ReportColumn("description",     "Description",  null,       null, "Left"),
        new ReportColumn("employee_amount", "Employee",     "currency", "C2", "Right"),
        new ReportColumn("employer_amount", "Employer",     "currency", "C2", "Right"),
        new ReportColumn("total",           "Total",        "currency", "C2", "Right"),
    };
}
