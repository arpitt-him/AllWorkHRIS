using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.Payroll;

/// <summary>
/// PAY-RPT-002 — Gross-to-Net Summary. Population totals by earnings,
/// deduction, tax, and employer-contribution category for a selected run.
/// </summary>
public sealed class PAY_RPT_002_GrossToNetSummary : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PAY_RPT_002_GrossToNetSummary(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "PAY-RPT-002";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "PAY-RPT-002",
        Title:        "Gross-to-Net Summary",
        ShortName:    "Gross to Net",
        Description:  "Population totals by earnings, deduction, tax, and employer-contribution category for a selected run.",
        AllowedRoles: new[] { "PayrollAdmin", "Finance", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:   true,
        IsWideReport: false);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        if (!p.RunId.HasValue)
            throw new ArgumentException("PAY-RPT-002 requires RunId.", nameof(p));

        const string sql = """
            SELECT 'Earnings'                         AS category,
                   erl.earnings_code                  AS code,
                   erl.earnings_description           AS description,
                   COUNT(DISTINCT erl.employment_id)  AS employee_count,
                   SUM(erl.calculated_amount)         AS total_amount
            FROM   earnings_result_line erl
            JOIN   employee_payroll_result epr ON epr.employee_payroll_result_id = erl.employee_payroll_result_id
            WHERE  epr.payroll_run_id = @RunId
              AND  (@LegalEntityId IS NULL OR EXISTS (
                       SELECT 1 FROM employment e WHERE e.employment_id = epr.employment_id AND e.legal_entity_id = @LegalEntityId))
            GROUP BY erl.earnings_code, erl.earnings_description
            UNION ALL
            SELECT CASE WHEN drl.pre_tax_flag THEN 'Pre-Tax Deduction' ELSE 'Post-Tax Deduction' END,
                   drl.deduction_code,
                   drl.deduction_description,
                   COUNT(DISTINCT drl.employment_id),
                   SUM(drl.calculated_amount)
            FROM   deduction_result_line drl
            JOIN   employee_payroll_result epr ON epr.employee_payroll_result_id = drl.employee_payroll_result_id
            WHERE  epr.payroll_run_id = @RunId
              AND  (@LegalEntityId IS NULL OR EXISTS (
                       SELECT 1 FROM employment e WHERE e.employment_id = epr.employment_id AND e.legal_entity_id = @LegalEntityId))
            GROUP BY drl.pre_tax_flag, drl.deduction_code, drl.deduction_description
            UNION ALL
            SELECT CASE WHEN trl.employer_flag THEN 'Employer Tax' ELSE 'Employee Tax' END,
                   trl.tax_code,
                   trl.tax_description,
                   COUNT(DISTINCT trl.employment_id),
                   SUM(trl.calculated_amount)
            FROM   tax_result_line trl
            JOIN   employee_payroll_result epr ON epr.employee_payroll_result_id = trl.employee_payroll_result_id
            WHERE  epr.payroll_run_id = @RunId
              AND  (@LegalEntityId IS NULL OR EXISTS (
                       SELECT 1 FROM employment e WHERE e.employment_id = epr.employment_id AND e.legal_entity_id = @LegalEntityId))
            GROUP BY trl.employer_flag, trl.tax_code, trl.tax_description
            UNION ALL
            SELECT 'Employer Contribution',
                   ecrl.contribution_code,
                   ecrl.contribution_description,
                   COUNT(DISTINCT ecrl.employment_id),
                   SUM(ecrl.calculated_amount)
            FROM   employer_contribution_result_line ecrl
            JOIN   employee_payroll_result epr ON epr.employee_payroll_result_id = ecrl.employee_payroll_result_id
            WHERE  epr.payroll_run_id = @RunId
              AND  (@LegalEntityId IS NULL OR EXISTS (
                       SELECT 1 FROM employment e WHERE e.employment_id = epr.employment_id AND e.legal_entity_id = @LegalEntityId))
            GROUP BY ecrl.contribution_code, ecrl.contribution_description
            ORDER BY category, code
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
        new ReportColumn("category",       "Category",    null,       null, "Left"),
        new ReportColumn("code",           "Code",        null,       null, "Left"),
        new ReportColumn("description",    "Description", null,       null, "Left"),
        new ReportColumn("employee_count", "EE Count",    "number",   "N0", "Right"),
        new ReportColumn("total_amount",   "Total",       "currency", "C2", "Right"),
    };
}
