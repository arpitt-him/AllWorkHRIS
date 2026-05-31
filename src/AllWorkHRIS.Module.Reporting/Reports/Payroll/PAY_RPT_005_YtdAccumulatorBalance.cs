using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.Payroll;

/// <summary>
/// PAY-RPT-005 — YTD Accumulator Balance. Per-employee YTD balances by
/// accumulator family, scoped to the user's selected legal entity.
/// </summary>
public sealed class PAY_RPT_005_YtdAccumulatorBalance : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PAY_RPT_005_YtdAccumulatorBalance(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "PAY-RPT-005";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "PAY-RPT-005",
        Title:        "YTD Accumulator Balance",
        ShortName:    "YTD Accumulators",
        Description:  "Per-employee YTD balances by accumulator family.",
        AllowedRoles: new[] { "PayrollOperator", "PayrollAdmin", "Finance", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    true,
        IsWideReport:  false,
        ParameterShape: ReportParameterShape.AsOfDate);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        const string sql = """
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name)  AS employee_name,
                e.employee_number                                        AS employee_number,
                f.label                                                  AS family,
                SUM(ab.current_value)                                    AS ytd_balance,
                MAX(ab.last_update_timestamp)                            AS last_updated
            FROM   accumulator_balance ab
            JOIN   lkp_accumulator_family f ON f.id = ab.accumulator_family_id
            JOIN   employment e             ON e.employment_id = ab.participant_id
            JOIN   person     per           ON per.person_id   = e.person_id
            JOIN   payroll_period pp        ON pp.period_id    = ab.calendar_context_id
            JOIN   accumulator_definition ad ON ad.accumulator_definition_id = ab.accumulator_definition_id
            WHERE  ab.participant_id IS NOT NULL
              AND  (CASE WHEN ad.year_basis = 'PAY_DATE'
                         THEN CAST(EXTRACT(YEAR FROM pp.pay_date) AS INT)
                         ELSE pp.period_year END) = CAST(EXTRACT(YEAR FROM CAST(@AsOf AS date)) AS INT)
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id = @LegalEntityId)
            GROUP BY per.legal_last_name, per.legal_first_name, e.employee_number, f.label, f.sort_order
            ORDER BY per.legal_last_name, per.legal_first_name, f.sort_order
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            p.LegalEntityId,
            AsOf = asOf.ToDateTime(TimeOnly.MinValue)
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
        new ReportColumn("employee_name",   "Employee",     null,       null,    "Left"),
        new ReportColumn("employee_number", "EE #",         null,       null,    "Left"),
        new ReportColumn("family",          "Family",       null,       null,    "Left"),
        new ReportColumn("ytd_balance",     "YTD Balance",  "currency", "C2",    "Right"),
        new ReportColumn("last_updated",    "Last Updated", "date",     "yMd",   "Left"),
    };
}
