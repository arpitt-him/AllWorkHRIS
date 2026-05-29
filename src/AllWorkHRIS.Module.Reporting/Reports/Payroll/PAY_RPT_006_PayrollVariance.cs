using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.Payroll;

/// <summary>
/// PAY-RPT-006 — Payroll Variance. Period-over-period gross pay delta vs.
/// the immediately prior run in the same payroll context. Optional
/// VarianceThreshold parameter flags rows whose abs(percent change) exceeds
/// the threshold.
/// </summary>
public sealed class PAY_RPT_006_PayrollVariance : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PAY_RPT_006_PayrollVariance(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "PAY-RPT-006";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "PAY-RPT-006",
        Title:        "Payroll Variance",
        ShortName:    "Payroll Variance",
        Description:  "Period-over-period gross pay delta vs. the prior run; flags rows above the variance threshold.",
        AllowedRoles: new[] { "PayrollAdmin", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:   false,
        IsWideReport: true);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        if (!p.RunId.HasValue)
            throw new ArgumentException("PAY-RPT-006 requires RunId.", nameof(p));

        // Pick the immediately prior released run in the same payroll context.
        const string sql = """
            WITH this_run AS (
                SELECT pr.run_id, pr.payroll_context_id, pr.pay_date
                FROM   payroll_run pr
                WHERE  pr.run_id = @RunId
            ),
            prior_run AS (
                SELECT pr.run_id
                FROM   payroll_run pr
                JOIN   this_run tr ON tr.payroll_context_id = pr.payroll_context_id
                JOIN   lkp_run_status rs ON rs.id = pr.run_status_id
                WHERE  pr.pay_date < tr.pay_date
                  AND  rs.code IN ('CALCULATED','UNDER_REVIEW','APPROVED','RELEASING','RELEASED','CLOSED')
                ORDER BY pr.pay_date DESC
                FETCH FIRST 1 ROWS ONLY
            ),
            curr AS (
                SELECT epr.employment_id, epr.gross_pay_amount AS gross
                FROM   employee_payroll_result epr
                WHERE  epr.payroll_run_id = @RunId
            ),
            prev AS (
                SELECT epr.employment_id, epr.gross_pay_amount AS gross
                FROM   employee_payroll_result epr
                WHERE  epr.payroll_run_id = (SELECT run_id FROM prior_run)
            )
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name)        AS employee_name,
                e.employee_number                                              AS employee_number,
                COALESCE(prev.gross, 0)                                        AS prior_gross,
                COALESCE(curr.gross, 0)                                        AS current_gross,
                COALESCE(curr.gross, 0) - COALESCE(prev.gross, 0)              AS delta,
                CASE WHEN COALESCE(prev.gross, 0) = 0 THEN NULL
                     ELSE (COALESCE(curr.gross, 0) - prev.gross) * 100.0 / prev.gross
                END                                                            AS percent_change,
                CASE
                    WHEN @VarianceThreshold IS NULL THEN 'n/a'
                    WHEN COALESCE(prev.gross, 0) = 0 THEN 'NEW'
                    WHEN ABS((COALESCE(curr.gross, 0) - prev.gross) * 100.0 / prev.gross) > @VarianceThreshold THEN 'FLAGGED'
                    ELSE 'OK'
                END                                                            AS flag
            FROM   curr
            FULL   OUTER JOIN prev ON prev.employment_id = curr.employment_id
            JOIN   employment e   ON e.employment_id = COALESCE(curr.employment_id, prev.employment_id)
            JOIN   person     per ON per.person_id   = e.person_id
            WHERE  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
            ORDER BY per.legal_last_name, per.legal_first_name
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            p.RunId,
            p.LegalEntityId,
            VarianceThreshold = (object?)p.VarianceThreshold ?? DBNull.Value
        });
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
        var threshold = p.VarianceThreshold.HasValue ? $" · Threshold {p.VarianceThreshold}%" : string.Empty;
        return row.ContextName is null
            ? string.Empty
            : $"Run: {row.PayDate:yyyy-MM-dd} — {row.ContextName}{threshold}";
    }

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("employee_name",   "Employee",     null,       null, "Left"),
        new ReportColumn("employee_number", "EE #",         null,       null, "Left"),
        new ReportColumn("prior_gross",     "Prior Gross",  "currency", "C2", "Right"),
        new ReportColumn("current_gross",   "Current Gross","currency", "C2", "Right"),
        new ReportColumn("delta",           "Delta",        "currency", "C2", "Right"),
        new ReportColumn("percent_change",  "%",            "number",   "N2", "Right"),
        new ReportColumn("flag",            "Flag",         null,       null, "Left"),
    };
}
