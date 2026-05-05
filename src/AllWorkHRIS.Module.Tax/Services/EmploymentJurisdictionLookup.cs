using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Pipeline;
using Dapper;

namespace AllWorkHRIS.Module.Tax.Services;

public sealed class EmploymentJurisdictionLookup : IEmploymentJurisdictionLookup
{
    private readonly IConnectionFactory _db;

    public EmploymentJurisdictionLookup(IConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<JurisdictionRef>> GetJurisdictionsAsync(
        Guid employmentId, DateOnly payDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT j.jurisdiction_id   AS JurisdictionId,
                            j.jurisdiction_code AS JurisdictionCode
            FROM   employee_tax_form_submission s
            JOIN   tax_jurisdiction j ON j.jurisdiction_id = s.jurisdiction_id
            WHERE  s.employment_id   = @EmploymentId
              AND  s.effective_from <= @PayDate
              AND  (s.effective_to  IS NULL OR s.effective_to >= @PayDate)
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<JurisdictionRef>(sql,
            new { EmploymentId = employmentId, PayDate = payDate });
        return rows.AsList();
    }
}
