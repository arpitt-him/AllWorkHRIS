using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Tax.Queries;
using AllWorkHRIS.Module.Tax.Repositories;
using Dapper;

namespace AllWorkHRIS.Module.Tax.Services;

public sealed class TaxFormSubmissionRepository : ITaxFormSubmissionRepository
{
    private readonly IConnectionFactory _db;

    public TaxFormSubmissionRepository(IConnectionFactory db) => _db = db;

    public async Task<EmployeeFilingProfile?> GetActiveProfileAsync(
        Guid employmentId, string jurisdictionCode, DateOnly payDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT d.filing_status_code     AS FilingStatusCode,
                   d.allowance_count        AS AllowanceCount,
                   d.additional_withholding AS AdditionalWithholding,
                   s.exempt_flag            AS ExemptFlag,
                   d.is_legacy_form         AS IsLegacyForm,
                   d.other_income_amount    AS OtherIncomeAmount,
                   d.deductions_amount      AS DeductionsAmount,
                   d.credits_amount         AS CreditsAmount,
                   d.claim_code             AS ClaimCode,
                   d.total_claim_amount     AS TotalClaimAmount,
                   d.additional_tax_amount  AS AdditionalTaxAmount
            FROM   employee_tax_form_submission s
            JOIN   employee_tax_form_detail     d ON d.submission_id  = s.submission_id
            JOIN   tax_jurisdiction             j ON j.jurisdiction_id = s.jurisdiction_id
            WHERE  s.employment_id      = @EmploymentId
              AND  j.jurisdiction_code  = @JurisdictionCode
              AND  s.effective_from    <= @PayDate
              AND  (s.effective_to     IS NULL OR s.effective_to >= @PayDate)
            LIMIT  1
            """;

        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<EmployeeFilingProfile>(sql,
            new { EmploymentId = employmentId, JurisdictionCode = jurisdictionCode, PayDate = payDate });
    }
}
