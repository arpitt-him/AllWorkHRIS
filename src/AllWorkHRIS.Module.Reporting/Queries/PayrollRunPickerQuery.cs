using AllWorkHRIS.Core.Data;
using Dapper;

namespace AllWorkHRIS.Module.Reporting.Queries;

public sealed record PayrollRunPickerRow(
    Guid     RunId,
    DateOnly PayDate,
    string   ContextName,
    string   Status)
{
    public string Label => $"{PayDate:yyyy-MM-dd} — {ContextName} — {Status}";
}

/// <summary>
/// Small read-side helper used by the Payroll Reports parameter panels to
/// populate a dropdown of recent runs in the user's selected legal entity.
/// Read-only; no FKs required across modules.
/// </summary>
public sealed class PayrollRunPickerQuery
{
    private readonly IConnectionFactory _connectionFactory;

    public PayrollRunPickerQuery(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<PayrollRunPickerRow>> GetRecentAsync(Guid? legalEntityId, int limit = 50)
    {
        // Only runs whose status has presentable results — calculated through
        // closed. Excludes Draft, Open, Calculating (no results yet), Failed
        // (unrecoverable), and Cancelled (discarded). Reports that need a
        // wider set can either widen this filter or use a different picker.
        const string sql = """
            SELECT pr.run_id                AS RunId,
                   pr.pay_date              AS PayDate,
                   pc.payroll_context_name  AS ContextName,
                   rs.label                 AS Status
            FROM   payroll_run pr
            JOIN   payroll_context pc ON pc.payroll_context_id = pr.payroll_context_id
            JOIN   lkp_run_status   rs ON rs.id = pr.run_status_id
            WHERE  (@LegalEntityId IS NULL OR pc.legal_entity_id = @LegalEntityId)
              AND  rs.code IN ('CALCULATED','UNDER_REVIEW','APPROVED','RELEASING','RELEASED','CLOSED')
            ORDER BY pr.pay_date DESC
            FETCH FIRST @Limit ROWS ONLY
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<PayrollRunPickerRow>(sql,
            new { LegalEntityId = legalEntityId, Limit = limit });
        return rows.ToList();
    }
}
