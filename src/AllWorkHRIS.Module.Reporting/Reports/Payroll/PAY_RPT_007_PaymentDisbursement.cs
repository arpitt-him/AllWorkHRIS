using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.Payroll;

/// <summary>
/// PAY-RPT-007 — Payment Disbursement. Per-check payment method, masked
/// account, net pay, and check status for the selected run.
/// </summary>
public sealed class PAY_RPT_007_PaymentDisbursement : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PAY_RPT_007_PaymentDisbursement(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "PAY-RPT-007";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "PAY-RPT-007",
        Title:        "Payment Disbursement",
        ShortName:    "Disbursements",
        Description:  "Per-check payment method, net pay, and disbursement status for the selected run.",
        AllowedRoles: new[] { "PayrollAdmin", "Finance", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:   true,
        IsWideReport: false);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        if (!p.RunId.HasValue)
            throw new ArgumentException("PAY-RPT-007 requires RunId.", nameof(p));

        const string sql = """
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name)  AS employee_name,
                e.employee_number                                        AS employee_number,
                pc.check_number                                          AS check_number,
                pm.label                                                 AS payment_method,
                pc.net_pay                                               AS net_pay,
                pc.payment_date                                          AS payment_date,
                cs.label                                                 AS check_status
            FROM   payroll_check pc
            JOIN   employment e             ON e.employment_id = pc.employment_id
            JOIN   person     per           ON per.person_id   = e.person_id
            JOIN   lkp_payment_method pm    ON pm.id = pc.payment_method_id
            JOIN   lkp_check_status   cs    ON cs.id = pc.check_status_id
            WHERE  pc.payroll_run_id = @RunId
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
            ORDER BY per.legal_last_name, per.legal_first_name
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
        new ReportColumn("employee_name",   "Employee",       null,       null,    "Left"),
        new ReportColumn("employee_number", "EE #",           null,       null,    "Left"),
        new ReportColumn("check_number",    "Check #",        null,       null,    "Left"),
        new ReportColumn("payment_method",  "Payment Method", null,       null,    "Left"),
        new ReportColumn("net_pay",         "Net Pay",        "currency", "C2",    "Right"),
        new ReportColumn("payment_date",    "Payment Date",   "date",     "yMd",   "Left"),
        new ReportColumn("check_status",    "Status",         null,       null,    "Left"),
    };
}
