using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.Payroll;

/// <summary>
/// PAY-RPT-004 — Payroll Exception Report. Exceptions encountered during a
/// selected payroll run — code, employee, and message.
/// </summary>
public sealed class PAY_RPT_004_PayrollException : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PAY_RPT_004_PayrollException(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "PAY-RPT-004";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "PAY-RPT-004",
        Title:        "Payroll Exception Report",
        ShortName:    "Payroll Exceptions",
        Description:  "Exceptions encountered during a selected payroll run, by code and employee.",
        AllowedRoles: new[] { "PayrollOperator", "PayrollAdmin", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:   false,
        IsWideReport: true);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        if (!p.RunId.HasValue)
            throw new ArgumentException("PAY-RPT-004 requires RunId.", nameof(p));

        const string sql = """
            SELECT
                pre.exception_code                                          AS exception_code,
                pre.exception_message                                       AS message,
                CONCAT(per.legal_last_name, ', ', per.legal_first_name)     AS employee_name,
                e.employee_number                                           AS employee_number,
                pre.created_timestamp                                       AS recorded_at
            FROM   payroll_run_exception pre
            JOIN   employment e   ON e.employment_id = pre.employment_id
            JOIN   person     per ON per.person_id   = e.person_id
            WHERE  pre.run_id = @RunId
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
            ORDER BY pre.exception_code, per.legal_last_name, per.legal_first_name
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
        new ReportColumn("exception_code",  "Exception Code", null, null,  "Left"),
        new ReportColumn("message",         "Message",        null, null,  "Left"),
        new ReportColumn("employee_name",   "Employee",       null, null,  "Left"),
        new ReportColumn("employee_number", "EE #",           null, null,  "Left"),
        new ReportColumn("recorded_at",     "Recorded",       "date", "yMd", "Left"),
    };
}
