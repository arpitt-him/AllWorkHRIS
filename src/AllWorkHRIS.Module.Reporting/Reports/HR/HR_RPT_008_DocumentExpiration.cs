using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Reports;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Reports.HR;

/// <summary>
/// HR-RPT-008 — Document Expiration. Documents whose expiration_date falls
/// within ExpirationDays of the as-of date (default 90).
/// </summary>
public sealed class HR_RPT_008_DocumentExpiration : IReportQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public HR_RPT_008_DocumentExpiration(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public string ReportId => "HR-RPT-008";

    public ReportDefinition Definition { get; } = new(
        ReportId:     "HR-RPT-008",
        Title:        "Document Expiration",
        ShortName:    "Doc Expiration",
        Description:  "Documents expiring within the selected window.",
        AllowedRoles: new[] { "HrisAdmin", "Auditor" },
        Columns:      ColumnDefs,
        ShowTotals:    false,
        IsWideReport:  false,
        ParameterShape: ReportParameterShape.Window);

    public async Task<ReportData> ExecuteAsync(ReportParameters p, DateOnly asOf, CancellationToken ct)
    {
        var windowDays = p.ExpirationDays ?? 90;
        var windowEnd  = asOf.AddDays(windowDays);

        const string sql = """
            SELECT
                CONCAT(per.legal_last_name, ', ', per.legal_first_name) AS employee_name,
                e.employee_number                                       AS employee_number,
                dt.label                                                AS document_type,
                d.document_name                                         AS document_name,
                d.expiration_date                                       AS expiration_date,
                (d.expiration_date - CAST(@AsOf AS date))               AS days_remaining
            FROM   document d
            JOIN   person     per ON per.person_id   = d.person_id
            LEFT   JOIN employment e ON e.employment_id = d.employment_id
            JOIN   lkp_document_type dt ON dt.id = d.document_type_id
            WHERE  d.expiration_date IS NOT NULL
              AND  d.expiration_date BETWEEN @AsOf AND @WindowEnd
              AND  (@LegalEntityId IS NULL OR e.legal_entity_id        = @LegalEntityId)
              AND  (@DepartmentId  IS NULL OR e.primary_department_id  = @DepartmentId)
            ORDER BY d.expiration_date, per.legal_last_name
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(sql, new
        {
            AsOf      = asOf.ToDateTime(TimeOnly.MinValue),
            WindowEnd = windowEnd.ToDateTime(TimeOnly.MinValue),
            p.LegalEntityId,
            p.DepartmentId
        });
        return ReportData.From(rows, ColumnDefs);
    }

    public Task<string> GetParameterSummaryAsync(ReportParameters p, CancellationToken ct)
    {
        var window = p.ExpirationDays ?? 90;
        return Task.FromResult($"Expiring within {window} days");
    }

    private static readonly IReadOnlyList<ReportColumn> ColumnDefs = new[]
    {
        new ReportColumn("employee_name",    "Employee",       null,     null,  "Left"),
        new ReportColumn("employee_number",  "EE #",           null,     null,  "Left"),
        new ReportColumn("document_type",    "Document Type",  null,     null,  "Left"),
        new ReportColumn("document_name",    "Document",       null,     null,  "Left"),
        new ReportColumn("expiration_date",  "Expires",        "date",   "yMd", "Left"),
        new ReportColumn("days_remaining",   "Days Remaining", "number", "N0",  "Right"),
    };
}
