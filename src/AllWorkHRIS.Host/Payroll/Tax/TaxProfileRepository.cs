using AllWorkHRIS.Core.Data;
using Dapper;

namespace AllWorkHRIS.Host.Payroll.Tax;

// ============================================================
// DOMAIN TYPES
// ============================================================

public sealed record TaxJurisdictionRow(
    int    JurisdictionId,
    string JurisdictionCode,
    string JurisdictionName,
    string CountryCode);

public sealed record EmployeeJurisdictionRow(
    int       JurisdictionId,
    string    JurisdictionCode,
    string    JurisdictionName,
    string    CountryCode,
    DateOnly? PendingEffectiveDate);

public sealed record MissingElectionRow(
    Guid    EmploymentId,
    string  LegalFirstName,
    string  LegalLastName,
    long    MissingCount,
    long    TotalCount,
    Guid?   PayrollContextId,
    string? PayrollContextName);

public sealed record TaxFilingStatusRow(
    string FilingStatusCode,
    string FilingStatusName);

public sealed record TaxProfileRow
{
    public Guid     SubmissionId         { get; init; }
    public string   JurisdictionCode     { get; init; } = default!;
    public string   JurisdictionName     { get; init; } = default!;
    public string   FormTypeCode         { get; init; } = default!;
    public bool     ExemptFlag           { get; init; }
    public DateOnly EffectiveFrom        { get; init; }

    // Detail
    public string?  FilingStatusCode     { get; init; }
    public int      AllowanceCount       { get; init; }
    public decimal  AdditionalWithholding { get; init; }
    public bool     IsLegacyForm         { get; init; }
    public decimal  OtherIncomeAmount    { get; init; }
    public decimal  DeductionsAmount     { get; init; }
    public decimal  CreditsAmount        { get; init; }
    public int?     ClaimCode            { get; init; }
    public decimal? TotalClaimAmount     { get; init; }
    public decimal  AdditionalTaxAmount  { get; init; }
}

public sealed class TaxProfileSaveModel
{
    public bool     ExemptFlag            { get; set; }
    public bool     IsLegacyForm          { get; set; }
    public string?  FilingStatusCode      { get; set; }
    public int      AllowanceCount        { get; set; }
    public decimal  AdditionalWithholding { get; set; }
    public decimal  OtherIncomeAmount     { get; set; }
    public decimal  DeductionsAmount      { get; set; }
    public decimal  CreditsAmount         { get; set; }
    public int?     ClaimCode             { get; set; }
    public decimal? TotalClaimAmount      { get; set; }
    public decimal  AdditionalTaxAmount   { get; set; }
}

// ============================================================
// INTERFACE
// ============================================================

public interface ITaxProfileRepository
{
    Task<IReadOnlyList<TaxJurisdictionRow>> GetAllJurisdictionsAsync();
    Task<IReadOnlyList<TaxJurisdictionRow>> GetJurisdictionsByLegalEntityAsync(Guid legalEntityId);
    Task<IReadOnlyList<EmployeeJurisdictionRow>> GetJurisdictionsByEmployeeAsync(Guid legalEntityId, Guid employmentId, DateOnly operativeDate, int lookAheadDays = 60);
    Task<IReadOnlyList<TaxFilingStatusRow>> GetFilingStatusesAsync(string jurisdictionCode);
    Task<IReadOnlyList<MissingElectionRow>> GetEmployeesMissingElectionsAsync(Guid legalEntityId, DateOnly operativeDate, int page, int pageSize);
    Task<TaxProfileRow?>                   GetActiveProfileAsync(Guid employmentId, string jurisdictionCode, DateOnly asOfDate);
    Task                                   SaveProfileAsync(Guid employmentId, string jurisdictionCode, TaxProfileSaveModel model, string createdBy, DateOnly effectiveFrom);
}

// ============================================================
// IMPLEMENTATION
// ============================================================

public sealed class TaxProfileRepository : ITaxProfileRepository
{
    private readonly IConnectionFactory _db;

    public TaxProfileRepository(IConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<TaxJurisdictionRow>> GetAllJurisdictionsAsync()
    {
        const string sql = """
            SELECT jurisdiction_id   AS JurisdictionId,
                   jurisdiction_code AS JurisdictionCode,
                   jurisdiction_name AS JurisdictionName,
                   country_code      AS CountryCode
            FROM   tax_jurisdiction
            WHERE  is_active = TRUE
            ORDER  BY country_code, jurisdiction_code
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<TaxJurisdictionRow>(sql);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TaxJurisdictionRow>> GetJurisdictionsByLegalEntityAsync(Guid legalEntityId)
    {
        const string sql = """
            SELECT j.jurisdiction_id   AS JurisdictionId,
                   j.jurisdiction_code AS JurisdictionCode,
                   j.jurisdiction_name AS JurisdictionName,
                   j.country_code      AS CountryCode
            FROM   legal_entity_jurisdiction lej
            JOIN   tax_jurisdiction j ON j.jurisdiction_id = lej.jurisdiction_id
            WHERE  lej.legal_entity_id = @LegalEntityId
              AND  j.is_active = TRUE
            ORDER  BY j.country_code, j.jurisdiction_code
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<TaxJurisdictionRow>(sql, new { LegalEntityId = legalEntityId });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<EmployeeJurisdictionRow>> GetJurisdictionsByEmployeeAsync(
        Guid legalEntityId, Guid employmentId, DateOnly operativeDate, int lookAheadDays = 60)
    {
        var operative   = operativeDate.ToDateTime(TimeOnly.MinValue);
        var lookAhead   = operativeDate.AddDays(lookAheadDays).ToDateTime(TimeOnly.MinValue);

        const string sql = """
            WITH current_home AS (
                SELECT pa.country_code, pa.state_code
                FROM   person_address pa
                JOIN   employment     e  ON e.person_id = pa.person_id
                WHERE  e.employment_id          = @EmploymentId
                  AND  pa.address_type          = 'PRIMARY'
                  AND  pa.effective_start_date <= @OperativeDate
                  AND  (pa.effective_end_date IS NULL OR pa.effective_end_date >= @OperativeDate)
            ),
            current_work AS (
                SELECT COALESCE(ou.country_code, 'US') AS country_code, ou.state_code
                FROM   employment e
                JOIN   org_unit   ou ON ou.org_unit_id = e.primary_work_location_id
                WHERE  e.employment_id = @EmploymentId
                  AND  ou.state_code IS NOT NULL
            ),
            upcoming_home AS (
                SELECT pa.country_code, pa.state_code, pa.effective_start_date AS pending_effective_date
                FROM   person_address pa
                JOIN   employment     e  ON e.person_id = pa.person_id
                WHERE  e.employment_id          = @EmploymentId
                  AND  pa.address_type          = 'PRIMARY'
                  AND  pa.effective_start_date  > @OperativeDate
                  AND  pa.effective_start_date <= @LookAheadDate
                  AND  NOT EXISTS (
                      SELECT 1 FROM current_home ch
                      WHERE ch.country_code = pa.country_code AND ch.state_code = pa.state_code
                  )
            )
            SELECT j.jurisdiction_id   AS JurisdictionId,
                   j.jurisdiction_code AS JurisdictionCode,
                   j.jurisdiction_name AS JurisdictionName,
                   j.country_code      AS CountryCode,
                   CAST(NULL AS date)  AS PendingEffectiveDate
            FROM   legal_entity_jurisdiction lej
            JOIN   tax_jurisdiction j ON j.jurisdiction_id = lej.jurisdiction_id
            WHERE  lej.legal_entity_id = @LegalEntityId
              AND  j.jurisdiction_code IN ('US-FED', 'CA-FED')
              AND  j.is_active = TRUE

            UNION ALL

            SELECT DISTINCT
                   j.jurisdiction_id   AS JurisdictionId,
                   j.jurisdiction_code AS JurisdictionCode,
                   j.jurisdiction_name AS JurisdictionName,
                   j.country_code      AS CountryCode,
                   CAST(NULL AS date)  AS PendingEffectiveDate
            FROM   (SELECT country_code, state_code FROM current_home
                    UNION
                    SELECT country_code, state_code FROM current_work) curr
            JOIN   tax_jurisdiction          j   ON j.jurisdiction_code = curr.country_code || '-' || curr.state_code
            JOIN   legal_entity_jurisdiction lej ON lej.jurisdiction_id = j.jurisdiction_id
            WHERE  lej.legal_entity_id = @LegalEntityId
              AND  j.is_active = TRUE

            UNION ALL

            SELECT j.jurisdiction_id   AS JurisdictionId,
                   j.jurisdiction_code AS JurisdictionCode,
                   j.jurisdiction_name AS JurisdictionName,
                   j.country_code      AS CountryCode,
                   uh.pending_effective_date AS PendingEffectiveDate
            FROM   upcoming_home             uh
            JOIN   tax_jurisdiction          j   ON j.jurisdiction_code = uh.country_code || '-' || uh.state_code
            JOIN   legal_entity_jurisdiction lej ON lej.jurisdiction_id = j.jurisdiction_id
            WHERE  lej.legal_entity_id = @LegalEntityId
              AND  j.is_active = TRUE

            ORDER BY PendingEffectiveDate NULLS FIRST, CountryCode, JurisdictionCode
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<EmployeeJurisdictionRow>(sql, new
        {
            LegalEntityId  = legalEntityId,
            EmploymentId   = employmentId,
            OperativeDate  = operative,
            LookAheadDate  = lookAhead
        });

        // If the same jurisdiction appears as both current (PendingEffectiveDate=null) and
        // upcoming, keep only the current entry — the state is already in scope.
        return rows
            .GroupBy(r => r.JurisdictionCode)
            .Select(g => g.FirstOrDefault(r => r.PendingEffectiveDate is null) ?? g.OrderBy(r => r.PendingEffectiveDate).First())
            .OrderBy(r => r.PendingEffectiveDate.HasValue)
            .ThenBy(r => r.CountryCode)
            .ThenBy(r => r.JurisdictionCode)
            .ToList();
    }

    public async Task<IReadOnlyList<MissingElectionRow>> GetEmployeesMissingElectionsAsync(
        Guid legalEntityId, DateOnly operativeDate, int page, int pageSize)
    {
        var asOf   = operativeDate.ToDateTime(TimeOnly.MinValue);
        var offset = (page - 1) * pageSize;

        const string sql = """
            WITH employee_required_jurisdictions AS (
                SELECT e.employment_id, lej.jurisdiction_id
                FROM   employment            e
                JOIN   lkp_employment_status es  ON es.id  = e.employment_status_id
                JOIN   legal_entity_jurisdiction lej ON lej.legal_entity_id = e.legal_entity_id
                JOIN   tax_jurisdiction       j   ON j.jurisdiction_id = lej.jurisdiction_id
                WHERE  e.legal_entity_id            = @LegalEntityId
                  AND  es.is_payroll_active          = TRUE
                  AND  e.payroll_eligibility_flag    = TRUE
                  AND  j.jurisdiction_code           IN ('US-FED','CA-FED')
                  AND  j.is_active                   = TRUE

                UNION

                SELECT e.employment_id, lej.jurisdiction_id
                FROM   employment            e
                JOIN   lkp_employment_status es  ON es.id  = e.employment_status_id
                JOIN   person_address        pa  ON pa.person_id = e.person_id
                JOIN   tax_jurisdiction       j   ON j.jurisdiction_code = pa.country_code || '-' || pa.state_code
                JOIN   legal_entity_jurisdiction lej ON lej.jurisdiction_id = j.jurisdiction_id
                WHERE  e.legal_entity_id            = @LegalEntityId
                  AND  es.is_payroll_active          = TRUE
                  AND  e.payroll_eligibility_flag    = TRUE
                  AND  pa.address_type               = 'PRIMARY'
                  AND  pa.effective_start_date       <= @AsOf
                  AND  (pa.effective_end_date IS NULL OR pa.effective_end_date >= @AsOf)
                  AND  lej.legal_entity_id           = e.legal_entity_id
                  AND  j.is_active                   = TRUE

                UNION

                SELECT e.employment_id, lej.jurisdiction_id
                FROM   employment            e
                JOIN   lkp_employment_status es  ON es.id  = e.employment_status_id
                JOIN   org_unit              ou  ON ou.org_unit_id = e.primary_work_location_id
                JOIN   tax_jurisdiction       j   ON j.jurisdiction_code = COALESCE(ou.country_code,'US') || '-' || ou.state_code
                JOIN   legal_entity_jurisdiction lej ON lej.jurisdiction_id = j.jurisdiction_id
                WHERE  e.legal_entity_id            = @LegalEntityId
                  AND  es.is_payroll_active          = TRUE
                  AND  e.payroll_eligibility_flag    = TRUE
                  AND  ou.state_code                 IS NOT NULL
                  AND  lej.legal_entity_id           = e.legal_entity_id
                  AND  j.is_active                   = TRUE
            ),
            missing_by_employee AS (
                SELECT erj.employment_id, COUNT(*) AS missing_count
                FROM   employee_required_jurisdictions erj
                WHERE  NOT EXISTS (
                    SELECT 1 FROM employee_tax_form_submission s
                    WHERE  s.employment_id   = erj.employment_id
                      AND  s.jurisdiction_id = erj.jurisdiction_id
                      AND  s.effective_from <= @AsOf
                      AND  (s.effective_to IS NULL OR s.effective_to >= @AsOf)
                )
                GROUP BY erj.employment_id
            )
            SELECT e.employment_id         AS EmploymentId,
                   p.legal_first_name      AS LegalFirstName,
                   p.legal_last_name       AS LegalLastName,
                   mbe.missing_count       AS MissingCount,
                   COUNT(*) OVER ()        AS TotalCount,
                   pc.payroll_context_id   AS PayrollContextId,
                   pc.payroll_context_name AS PayrollContextName
            FROM   missing_by_employee mbe
            JOIN   employment     e   ON e.employment_id      = mbe.employment_id
            JOIN   person         p   ON p.person_id          = e.person_id
            LEFT JOIN payroll_profile pp  ON pp.employment_id = e.employment_id
                                         AND pp.enrollment_status = 'ACTIVE'
            LEFT JOIN payroll_context pc  ON pc.payroll_context_id = pp.payroll_context_id
            ORDER BY pc.payroll_context_name NULLS LAST, p.legal_last_name, p.legal_first_name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<MissingElectionRow>(sql, new
        {
            LegalEntityId = legalEntityId,
            AsOf          = asOf,
            Offset        = offset,
            PageSize      = pageSize
        });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TaxFilingStatusRow>> GetFilingStatusesAsync(string jurisdictionCode)
    {
        const string sql = """
            SELECT f.filing_status_code AS FilingStatusCode,
                   f.filing_status_name AS FilingStatusName
            FROM   tax_filing_status f
            JOIN   tax_jurisdiction  j ON j.jurisdiction_id = f.jurisdiction_id
            WHERE  j.jurisdiction_code = @JurisdictionCode
            ORDER  BY f.filing_status_code
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<TaxFilingStatusRow>(sql, new { JurisdictionCode = jurisdictionCode });
        return rows.AsList();
    }

    public async Task<TaxProfileRow?> GetActiveProfileAsync(
        Guid employmentId, string jurisdictionCode, DateOnly asOfDate)
    {
        const string sql = """
            SELECT s.submission_id           AS SubmissionId,
                   j.jurisdiction_code       AS JurisdictionCode,
                   j.jurisdiction_name       AS JurisdictionName,
                   t.code                    AS FormTypeCode,
                   s.exempt_flag             AS ExemptFlag,
                   s.effective_from          AS EffectiveFrom,
                   d.filing_status_code      AS FilingStatusCode,
                   d.allowance_count         AS AllowanceCount,
                   d.additional_withholding  AS AdditionalWithholding,
                   d.is_legacy_form          AS IsLegacyForm,
                   d.other_income_amount     AS OtherIncomeAmount,
                   d.deductions_amount       AS DeductionsAmount,
                   d.credits_amount          AS CreditsAmount,
                   d.claim_code              AS ClaimCode,
                   d.total_claim_amount      AS TotalClaimAmount,
                   d.additional_tax_amount   AS AdditionalTaxAmount
            FROM   employee_tax_form_submission s
            JOIN   tax_jurisdiction             j ON j.jurisdiction_id = s.jurisdiction_id
            JOIN   lkp_tax_form_type            t ON t.id = s.form_type_id
            JOIN   employee_tax_form_detail     d ON d.submission_id = s.submission_id
            WHERE  s.employment_id      = @EmploymentId
              AND  j.jurisdiction_code  = @JurisdictionCode
              AND  s.effective_from    <= @AsOfDate
              AND  (s.effective_to     IS NULL OR s.effective_to >= @AsOfDate)
            LIMIT  1
            """;
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<TaxProfileRow>(sql,
            new
            {
                EmploymentId     = employmentId,
                JurisdictionCode = jurisdictionCode,
                AsOfDate         = asOfDate.ToDateTime(TimeOnly.MinValue)
            });
    }

    public async Task SaveProfileAsync(
        Guid employmentId, string jurisdictionCode,
        TaxProfileSaveModel model, string createdBy, DateOnly effectiveFrom)
    {
        var jurisdictionId = await ResolveJurisdictionIdAsync(jurisdictionCode);
        var formTypeCode   = ResolveFormTypeCode(jurisdictionCode, model.IsLegacyForm);
        var formTypeId     = await ResolveFormTypeIdAsync(formTypeCode);
        var newId          = Guid.NewGuid();
        var closeDate      = effectiveFrom.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var effFrom        = effectiveFrom.ToDateTime(TimeOnly.MinValue);
        var now            = DateTimeOffset.UtcNow;

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        // Close any currently open submission for this employment/jurisdiction
        const string closeSql = """
            UPDATE employee_tax_form_submission
            SET    effective_to = @ClosedDate
            WHERE  employment_id   = @EmploymentId
              AND  jurisdiction_id = @JurisdictionId
              AND  effective_to    IS NULL
            """;
        await conn.ExecuteAsync(closeSql,
            new { EmploymentId = employmentId, JurisdictionId = jurisdictionId, ClosedDate = closeDate },
            tx);

        // Insert new submission
        const string insertSql = """
            INSERT INTO employee_tax_form_submission
              (submission_id, employment_id, jurisdiction_id, form_type_id,
               exempt_flag, submitted_date, effective_from, created_by, creation_timestamp)
            VALUES
              (@SubmissionId, @EmploymentId, @JurisdictionId, @FormTypeId,
               @ExemptFlag, @SubmittedDate, @EffectiveFrom, @CreatedBy, @Now)
            """;
        await conn.ExecuteAsync(insertSql,
            new
            {
                SubmissionId  = newId,
                EmploymentId  = employmentId,
                JurisdictionId = jurisdictionId,
                FormTypeId    = formTypeId,
                ExemptFlag    = model.ExemptFlag,
                SubmittedDate = effFrom,
                EffectiveFrom = effFrom,
                CreatedBy     = createdBy,
                Now           = now
            }, tx);

        // Insert detail
        const string detailSql = """
            INSERT INTO employee_tax_form_detail
              (submission_id, filing_status_code, allowance_count, additional_withholding,
               is_legacy_form, other_income_amount, deductions_amount, credits_amount,
               claim_code, total_claim_amount, additional_tax_amount)
            VALUES
              (@SubmissionId, @FilingStatusCode, @AllowanceCount, @AdditionalWithholding,
               @IsLegacyForm, @OtherIncomeAmount, @DeductionsAmount, @CreditsAmount,
               @ClaimCode, @TotalClaimAmount, @AdditionalTaxAmount)
            """;
        await conn.ExecuteAsync(detailSql,
            new
            {
                SubmissionId          = newId,
                FilingStatusCode      = model.FilingStatusCode,
                AllowanceCount        = model.AllowanceCount,
                AdditionalWithholding = model.AdditionalWithholding,
                IsLegacyForm          = model.IsLegacyForm,
                OtherIncomeAmount     = model.OtherIncomeAmount,
                DeductionsAmount      = model.DeductionsAmount,
                CreditsAmount         = model.CreditsAmount,
                ClaimCode             = model.ClaimCode,
                TotalClaimAmount      = model.TotalClaimAmount,
                AdditionalTaxAmount   = model.AdditionalTaxAmount
            }, tx);

        tx.Commit();
    }

    private async Task<int> ResolveJurisdictionIdAsync(string jurisdictionCode)
    {
        const string sql = "SELECT jurisdiction_id FROM tax_jurisdiction WHERE jurisdiction_code = @Code";
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, new { Code = jurisdictionCode });
    }

    private async Task<int> ResolveFormTypeIdAsync(string formTypeCode)
    {
        const string sql = "SELECT id FROM lkp_tax_form_type WHERE code = @Code";
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, new { Code = formTypeCode });
    }

    private static string ResolveFormTypeCode(string jurisdictionCode, bool isLegacy) => jurisdictionCode switch
    {
        "BB"     => "BB_TD4",
        "CA-FED" => "TD1",
        "US-FED" => isLegacy ? "W4_LEGACY" : "W4_2020",
        "US-GA"  => "G_4",
        "US-NY"  => "IT_2104",
        "US-CA"  => "DE_4",
        _        => "W4_2020"
    };
}
