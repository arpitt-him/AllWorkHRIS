using AllWorkHRIS.Core.Data;
using Dapper;

namespace AllWorkHRIS.Host.Config.Tax;

// ── Domain types ────────────────────────────────────────────────────

public sealed record TaxStepConfigRow
{
    public int    StepId                  { get; init; }
    public string StepCode                { get; init; } = default!;
    public string StepName                { get; init; } = default!;
    public string StepType                { get; init; } = default!;
    public string CalculationCategory     { get; init; } = default!;
    public int    SequenceNumber          { get; init; }
    public string AppliesTo               { get; init; } = default!;
    public bool   ReducesIncomeTaxWages   { get; init; }
    public bool   ReducesFicaWages        { get; init; }
    public string StatusCode              { get; init; } = default!;
    public bool   IsActive                { get; init; }
    public string JurisdictionCode        { get; init; } = default!;
    public string JurisdictionName        { get; init; } = default!;
}

public sealed record TaxRateRowSummary
{
    public string   RowType       { get; init; } = default!;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo  { get; init; }
    public string   Summary       { get; init; } = default!;
}

public sealed record FormFieldConfigRow
{
    public int     FieldDefinitionId { get; init; }
    public string  FormTypeCode      { get; init; } = default!;
    public string  FieldKey          { get; init; } = default!;
    public string  DisplayLabel      { get; init; } = default!;
    public string  FieldType         { get; init; } = default!;
    public int     DisplayOrder      { get; init; }
    public bool    IsRequired        { get; init; }
    public string  StatusCode        { get; init; } = default!;
    public bool    IsActive          { get; init; }
    public string? SectionLabel      { get; init; }
    public string? HelpText          { get; init; }
    public DateOnly EffectiveFrom    { get; init; }
}

public sealed record PendingReviewItem
{
    public string   EntityType      { get; init; } = default!;
    public string   EntityId        { get; init; } = default!;
    public string   EntityName      { get; init; } = default!;
    public string   JurisdictionCode { get; init; } = default!;
    public string   SubmittedBy     { get; init; } = default!;
    public DateTime SubmittedAt     { get; init; }
}

public sealed record AuditLogRow
{
    public int      AuditLogId      { get; init; }
    public string   ActionCode      { get; init; } = default!;
    public string?  PriorStatus     { get; init; }
    public string?  NewStatus       { get; init; }
    public string   ChangedBy       { get; init; } = default!;
    public DateTime ChangedAt       { get; init; }
    public string?  ApprovalNote    { get; init; }
}

// ── Rate admin domain types ──────────────────────────────────────────

public sealed record FlatRateDetailRow(
    int       FlatRateId,
    DateOnly  EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal   Rate,
    decimal?  WageBase,
    decimal?  PeriodCap,
    decimal?  AnnualCap,
    string?   DependsOnStepCode);

public sealed record BracketDetailRow(
    int       BracketId,
    DateOnly  EffectiveFrom,
    DateOnly? EffectiveTo,
    string?   FilingStatusCode,
    decimal   LowerLimit,
    decimal?  UpperLimit,
    decimal   Rate);

public sealed record AllowanceDetailRow(
    int       AllowanceId,
    DateOnly  EffectiveFrom,
    DateOnly? EffectiveTo,
    string?   FilingStatusCode,
    decimal   AnnualAmount);

public sealed record TieredBracketDetailRow(
    int       TieredBracketId,
    DateOnly  EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal   LowerLimit,
    decimal?  UpperLimit,
    decimal   Rate,
    decimal?  PeriodCap,
    decimal?  AnnualCap);

public sealed record CreditDetailRow(
    int       CreditId,
    DateOnly  EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal   AnnualAmount,
    decimal   CreditRate,
    bool      IsRefundable);

public sealed record BracketInput(
    string?  FilingStatusCode,
    decimal  LowerLimit,
    decimal? UpperLimit,
    decimal  Rate);

public sealed record TieredBracketInput(
    decimal  LowerLimit,
    decimal? UpperLimit,
    decimal  Rate,
    decimal? PeriodCap,
    decimal? AnnualCap);

// ── Interface ───────────────────────────────────────────────────────

public interface ITaxConfigRepository
{
    Task<IReadOnlyList<TaxStepConfigRow>>   GetStepsByJurisdictionAsync(string jurisdictionCode);
    Task<IReadOnlyList<TaxStepConfigRow>>   GetAllActiveStepsAsync();
    Task<IReadOnlyList<TaxRateRowSummary>>  GetRateSummaryAsync(string stepCode);
    Task<IReadOnlyList<FormFieldConfigRow>> GetFormFieldsAsync(string formTypeCode);
    Task<IReadOnlyList<PendingReviewItem>>  GetPendingReviewItemsAsync();
    Task<IReadOnlyList<AuditLogRow>>        GetAuditLogAsync(string entityName, string entityId, int limit = 20);

    Task UpdateStepStatusAsync(int stepId, string stepCode, string priorStatus, string newStatus, string actor, string? note = null);
    Task UpdateFormFieldRequiredAsync(int fieldId, bool isRequired, string actor);
    Task UpdateFormFieldStatusAsync(int fieldId, string fieldKey, string priorStatus, string newStatus, string actor, string? note = null);

    // ── Rate admin reads ─────────────────────────────────────────────
    Task<IReadOnlyList<FlatRateDetailRow>>      GetFlatRatesAsync(string stepCode);
    Task<IReadOnlyList<BracketDetailRow>>       GetBracketsDetailAsync(string stepCode);
    Task<IReadOnlyList<AllowanceDetailRow>>     GetAllowancesDetailAsync(string stepCode);
    Task<IReadOnlyList<TieredBracketDetailRow>> GetTieredBracketsDetailAsync(string stepCode);
    Task<IReadOnlyList<CreditDetailRow>>        GetCreditsDetailAsync(string stepCode);
    Task<IReadOnlySet<string>>                  GetExpiringStepCodesAsync(DateOnly asOf, int daysAhead = 120);

    // ── Rate admin writes — flat rate ────────────────────────────────
    Task InsertFlatRateAsync(string stepCode, DateOnly effectiveFrom, decimal rate, decimal? wageBase, decimal? periodCap, decimal? annualCap, string actor);
    Task CloseFlatRateAsync(int flatRateId, DateOnly effectiveTo, string actor);

    // ── Rate admin writes — brackets ─────────────────────────────────
    Task InsertBracketSetAsync(string stepCode, DateOnly effectiveFrom, IReadOnlyList<BracketInput> rows, string actor);
    Task CloseBracketSetAsync(string stepCode, DateOnly effectiveFrom, DateOnly effectiveTo, string actor);

    // ── Rate admin writes — allowances ───────────────────────────────
    Task InsertAllowanceAsync(string stepCode, DateOnly effectiveFrom, string? filingStatusCode, decimal annualAmount, string actor);
    Task CloseAllowanceAsync(int allowanceId, DateOnly effectiveTo, string actor);

    // ── Rate admin writes — tiered brackets ──────────────────────────
    Task InsertTieredBracketSetAsync(string stepCode, DateOnly effectiveFrom, IReadOnlyList<TieredBracketInput> rows, string actor);
    Task CloseTieredBracketSetAsync(string stepCode, DateOnly effectiveFrom, DateOnly effectiveTo, string actor);

    // ── Rate admin writes — credits ──────────────────────────────────
    Task InsertCreditAsync(string stepCode, DateOnly effectiveFrom, decimal annualAmount, decimal creditRate, bool isRefundable, string actor);
    Task CloseCreditAsync(int creditId, DateOnly effectiveTo, string actor);
}

// ── Implementation ──────────────────────────────────────────────────

public sealed class TaxConfigRepository : ITaxConfigRepository
{
    private readonly IConnectionFactory _db;
    public TaxConfigRepository(IConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<TaxStepConfigRow>> GetStepsByJurisdictionAsync(string jurisdictionCode)
    {
        const string sql = """
            SELECT s.step_id                  AS StepId,
                   s.step_code                AS StepCode,
                   s.step_name                AS StepName,
                   s.step_type               AS StepType,
                   s.calculation_category    AS CalculationCategory,
                   s.sequence_number         AS SequenceNumber,
                   s.applies_to              AS AppliesTo,
                   s.reduces_income_tax_wages AS ReducesIncomeTaxWages,
                   s.reduces_fica_wages       AS ReducesFicaWages,
                   s.status_code             AS StatusCode,
                   s.is_active               AS IsActive,
                   j.jurisdiction_code       AS JurisdictionCode,
                   j.jurisdiction_name       AS JurisdictionName
            FROM   payroll_calculation_steps s
            JOIN   tax_jurisdiction j ON j.jurisdiction_id = s.jurisdiction_id
            WHERE  j.jurisdiction_code = @JurisdictionCode
            ORDER  BY s.sequence_number
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<TaxStepConfigRow>(sql, new { JurisdictionCode = jurisdictionCode })).AsList();
    }

    public async Task<IReadOnlyList<TaxStepConfigRow>> GetAllActiveStepsAsync()
    {
        const string sql = """
            SELECT s.step_id                  AS StepId,
                   s.step_code                AS StepCode,
                   s.step_name                AS StepName,
                   s.step_type               AS StepType,
                   s.calculation_category    AS CalculationCategory,
                   s.sequence_number         AS SequenceNumber,
                   s.applies_to              AS AppliesTo,
                   s.reduces_income_tax_wages AS ReducesIncomeTaxWages,
                   s.reduces_fica_wages       AS ReducesFicaWages,
                   s.status_code             AS StatusCode,
                   s.is_active               AS IsActive,
                   j.jurisdiction_code       AS JurisdictionCode,
                   j.jurisdiction_name       AS JurisdictionName
            FROM   payroll_calculation_steps s
            JOIN   tax_jurisdiction j ON j.jurisdiction_id = s.jurisdiction_id
            WHERE  s.status_code = 'ACTIVE'
            ORDER  BY j.jurisdiction_code, s.sequence_number
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<TaxStepConfigRow>(sql)).AsList();
    }

    public async Task<IReadOnlyList<TaxRateRowSummary>> GetRateSummaryAsync(string stepCode)
    {
        var results = new List<TaxRateRowSummary>();

        const string flatSql = """
            SELECT effective_from, effective_to, rate, wage_base, period_cap_amount, annual_cap_amount
            FROM   tax_flat_rates WHERE step_code = @StepCode ORDER BY effective_from DESC
            """;
        const string bracketSql = """
            SELECT effective_from, effective_to, COUNT(*) AS cnt
            FROM   tax_brackets WHERE step_code = @StepCode
            GROUP  BY effective_from, effective_to ORDER BY effective_from DESC
            """;
        const string tieredSql = """
            SELECT effective_from, effective_to, COUNT(*) AS cnt
            FROM   tax_tiered_brackets WHERE step_code = @StepCode
            GROUP  BY effective_from, effective_to ORDER BY effective_from DESC
            """;
        const string allowSql = """
            SELECT effective_from, effective_to, annual_amount, filing_status_code
            FROM   tax_allowances WHERE step_code = @StepCode ORDER BY effective_from DESC
            """;
        const string creditSql = """
            SELECT effective_from, effective_to, annual_amount, credit_rate
            FROM   tax_credits WHERE step_code = @StepCode ORDER BY effective_from DESC
            """;

        var p = new { StepCode = stepCode };
        using var conn = _db.CreateConnection();

        foreach (var r in await conn.QueryAsync(flatSql, p))
        {
            results.Add(new TaxRateRowSummary
            {
                RowType       = "Flat Rate",
                EffectiveFrom = (DateOnly)r.effective_from,
                EffectiveTo   = r.effective_to is null ? null : (DateOnly?)r.effective_to,
                Summary       = $"Rate {(decimal)r.rate:P4}" +
                                (r.wage_base       is not null ? $" · Wage base {(decimal)r.wage_base:N2}" : "") +
                                (r.annual_cap_amount is not null ? $" · Annual cap {(decimal)r.annual_cap_amount:N2}" : "")
            });
        }

        foreach (var r in await conn.QueryAsync(bracketSql, p))
        {
            results.Add(new TaxRateRowSummary
            {
                RowType       = "Brackets",
                EffectiveFrom = (DateOnly)r.effective_from,
                EffectiveTo   = r.effective_to is null ? null : (DateOnly?)r.effective_to,
                Summary       = $"{(long)r.cnt} bracket rows"
            });
        }

        foreach (var r in await conn.QueryAsync(tieredSql, p))
        {
            results.Add(new TaxRateRowSummary
            {
                RowType       = "Tiered",
                EffectiveFrom = (DateOnly)r.effective_from,
                EffectiveTo   = r.effective_to is null ? null : (DateOnly?)r.effective_to,
                Summary       = $"{(long)r.cnt} tier rows"
            });
        }

        foreach (var r in await conn.QueryAsync(allowSql, p))
        {
            results.Add(new TaxRateRowSummary
            {
                RowType       = "Allowance",
                EffectiveFrom = (DateOnly)r.effective_from,
                EffectiveTo   = r.effective_to is null ? null : (DateOnly?)r.effective_to,
                Summary       = $"{(decimal)r.annual_amount:N2}/yr" +
                                (r.filing_status_code is not null ? $" ({r.filing_status_code})" : "")
            });
        }

        foreach (var r in await conn.QueryAsync(creditSql, p))
        {
            results.Add(new TaxRateRowSummary
            {
                RowType       = "Credit",
                EffectiveFrom = (DateOnly)r.effective_from,
                EffectiveTo   = r.effective_to is null ? null : (DateOnly?)r.effective_to,
                Summary       = $"{(decimal)r.annual_amount:N2} · Rate {(decimal)r.credit_rate:P4}"
            });
        }

        return results.OrderByDescending(r => r.EffectiveFrom).ToList();
    }

    public async Task<IReadOnlyList<FormFieldConfigRow>> GetFormFieldsAsync(string formTypeCode)
    {
        const string sql = """
            SELECT field_definition_id AS FieldDefinitionId,
                   form_type_code      AS FormTypeCode,
                   field_key           AS FieldKey,
                   display_label       AS DisplayLabel,
                   field_type          AS FieldType,
                   display_order       AS DisplayOrder,
                   is_required         AS IsRequired,
                   status_code         AS StatusCode,
                   is_active           AS IsActive,
                   section_label       AS SectionLabel,
                   help_text           AS HelpText,
                   effective_from      AS EffectiveFrom
            FROM   form_field_definition
            WHERE  form_type_code = @FormTypeCode
            ORDER  BY display_order
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<FormFieldConfigRow>(sql, new { FormTypeCode = formTypeCode })).AsList();
    }

    public async Task<IReadOnlyList<PendingReviewItem>> GetPendingReviewItemsAsync()
    {
        var items = new List<PendingReviewItem>();

        const string stepSql = """
            SELECT s.step_id           AS "EntityId",
                   s.step_code         AS "EntityName",
                   s.step_name         AS "StepName",
                   j.jurisdiction_code  AS "JurisdictionCode",
                   l.changed_by        AS "SubmittedBy",
                   l.changed_at        AS "SubmittedAt"
            FROM   payroll_calculation_steps s
            JOIN   tax_jurisdiction j ON j.jurisdiction_id = s.jurisdiction_id
            LEFT  JOIN configuration_audit_log l
                  ON  l.entity_name    = 'payroll_calculation_steps'
                  AND l.entity_id_text = CAST(s.step_id AS VARCHAR)
                  AND l.action_code    = 'SUBMITTED_FOR_REVIEW'
            WHERE  s.status_code = 'PENDING_REVIEW'
            ORDER  BY l.changed_at DESC
            """;

        const string fieldSql = """
            SELECT f.field_definition_id AS "EntityId",
                   f.field_key            AS "EntityName",
                   f.display_label        AS "StepName",
                   f.form_type_code       AS "JurisdictionCode",
                   l.changed_by           AS "SubmittedBy",
                   l.changed_at           AS "SubmittedAt"
            FROM   form_field_definition f
            LEFT  JOIN configuration_audit_log l
                  ON  l.entity_name    = 'form_field_definition'
                  AND l.entity_id_text = CAST(f.field_definition_id AS VARCHAR)
                  AND l.action_code    = 'SUBMITTED_FOR_REVIEW'
            WHERE  f.status_code = 'PENDING_REVIEW'
            ORDER  BY l.changed_at DESC
            """;

        using var conn = _db.CreateConnection();

        foreach (var r in await conn.QueryAsync(stepSql))
        {
            items.Add(new PendingReviewItem
            {
                EntityType       = "step",
                EntityId         = r.EntityId.ToString(),
                EntityName       = $"{r.StepName} ({r.EntityName})",
                JurisdictionCode = r.JurisdictionCode,
                SubmittedBy      = r.SubmittedBy ?? "—",
                SubmittedAt      = r.SubmittedAt ?? DateTime.MinValue
            });
        }

        foreach (var r in await conn.QueryAsync(fieldSql))
        {
            items.Add(new PendingReviewItem
            {
                EntityType       = "form_field",
                EntityId         = r.EntityId.ToString(),
                EntityName       = $"{r.StepName} ({r.EntityName})",
                JurisdictionCode = r.JurisdictionCode,
                SubmittedBy      = r.SubmittedBy ?? "—",
                SubmittedAt      = r.SubmittedAt ?? DateTime.MinValue
            });
        }

        return items.OrderByDescending(i => i.SubmittedAt).ToList();
    }

    public async Task<IReadOnlyList<AuditLogRow>> GetAuditLogAsync(string entityName, string entityId, int limit = 20)
    {
        const string sql = """
            SELECT audit_log_id    AS AuditLogId,
                   action_code     AS ActionCode,
                   prior_status_code AS PriorStatus,
                   new_status_code AS NewStatus,
                   changed_by      AS ChangedBy,
                   changed_at      AS ChangedAt,
                   approval_note   AS ApprovalNote
            FROM   configuration_audit_log
            WHERE  entity_name    = @EntityName
              AND  entity_id_text = @EntityId
            ORDER  BY changed_at DESC
            FETCH  FIRST @Limit ROWS ONLY
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<AuditLogRow>(sql, new { EntityName = entityName, EntityId = entityId, Limit = limit })).AsList();
    }

    public async Task UpdateStepStatusAsync(
        int stepId, string stepCode, string priorStatus, string newStatus, string actor, string? note = null)
    {
        const string updateSql = """
            UPDATE payroll_calculation_steps
            SET    status_code = @NewStatus,
                   is_active   = @IsActive
            WHERE  step_id = @StepId
            """;
        const string auditSql = """
            INSERT INTO configuration_audit_log
              (entity_name, entity_id_text, action_code, prior_status_code, new_status_code,
               changed_by, changed_at, approval_note)
            VALUES
              ('payroll_calculation_steps', @EntityId, @ActionCode, @PriorStatus, @NewStatus,
               @Actor, @Now, @Note)
            """;

        var isActive = newStatus == "ACTIVE";
        var action   = StatusToAction(priorStatus, newStatus);
        var now      = DateTime.UtcNow;

        using var conn = _db.CreateConnection();
        using var tx   = conn.BeginTransaction();
        await conn.ExecuteAsync(updateSql, new { NewStatus = newStatus, IsActive = isActive, StepId = stepId }, tx);
        await conn.ExecuteAsync(auditSql,
            new { EntityId = stepId.ToString(), ActionCode = action, PriorStatus = priorStatus,
                  NewStatus = newStatus, Actor = actor, Now = now, Note = note }, tx);
        tx.Commit();
    }

    public async Task UpdateFormFieldRequiredAsync(int fieldId, bool isRequired, string actor)
    {
        const string sql = """
            UPDATE form_field_definition SET is_required = @IsRequired WHERE field_definition_id = @FieldId
            """;
        const string auditSql = """
            INSERT INTO configuration_audit_log
              (entity_name, entity_id_text, action_code, changed_by, changed_at, approval_note)
            VALUES
              ('form_field_definition', @EntityId, 'EDITED', @Actor, @Now, @Note)
            """;
        using var conn = _db.CreateConnection();
        using var tx   = conn.BeginTransaction();
        await conn.ExecuteAsync(sql, new { IsRequired = isRequired, FieldId = fieldId }, tx);
        await conn.ExecuteAsync(auditSql,
            new { EntityId = fieldId.ToString(), Actor = actor,
                  Now = DateTime.UtcNow, Note = $"is_required → {isRequired}" }, tx);
        tx.Commit();
    }

    public async Task UpdateFormFieldStatusAsync(
        int fieldId, string fieldKey, string priorStatus, string newStatus, string actor, string? note = null)
    {
        const string sql = """
            UPDATE form_field_definition
            SET    status_code = @NewStatus, is_active = @IsActive
            WHERE  field_definition_id = @FieldId
            """;
        const string auditSql = """
            INSERT INTO configuration_audit_log
              (entity_name, entity_id_text, action_code, prior_status_code, new_status_code,
               changed_by, changed_at, approval_note)
            VALUES
              ('form_field_definition', @EntityId, @ActionCode, @PriorStatus, @NewStatus,
               @Actor, @Now, @Note)
            """;
        var isActive = newStatus == "ACTIVE";
        var action   = StatusToAction(priorStatus, newStatus);
        using var conn = _db.CreateConnection();
        using var tx   = conn.BeginTransaction();
        await conn.ExecuteAsync(sql, new { NewStatus = newStatus, IsActive = isActive, FieldId = fieldId }, tx);
        await conn.ExecuteAsync(auditSql,
            new { EntityId = fieldId.ToString(), ActionCode = action, PriorStatus = priorStatus,
                  NewStatus = newStatus, Actor = actor, Now = DateTime.UtcNow, Note = note }, tx);
        tx.Commit();
    }

    // ── Rate admin reads ─────────────────────────────────────────────

    public async Task<IReadOnlyList<FlatRateDetailRow>> GetFlatRatesAsync(string stepCode)
    {
        const string sql = """
            SELECT flat_rate_id       AS FlatRateId,
                   effective_from     AS EffectiveFrom,
                   effective_to       AS EffectiveTo,
                   rate               AS Rate,
                   wage_base          AS WageBase,
                   period_cap_amount  AS PeriodCap,
                   annual_cap_amount  AS AnnualCap,
                   depends_on_step_code AS DependsOnStepCode
            FROM   tax_flat_rates
            WHERE  step_code = @StepCode
            ORDER  BY effective_from DESC
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<FlatRateDetailRow>(sql, new { StepCode = stepCode })).AsList();
    }

    public async Task<IReadOnlyList<BracketDetailRow>> GetBracketsDetailAsync(string stepCode)
    {
        const string sql = """
            SELECT bracket_id         AS BracketId,
                   effective_from     AS EffectiveFrom,
                   effective_to       AS EffectiveTo,
                   filing_status_code AS FilingStatusCode,
                   lower_limit        AS LowerLimit,
                   upper_limit        AS UpperLimit,
                   rate               AS Rate
            FROM   tax_brackets
            WHERE  step_code = @StepCode
            ORDER  BY effective_from DESC, lower_limit
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<BracketDetailRow>(sql, new { StepCode = stepCode })).AsList();
    }

    public async Task<IReadOnlyList<AllowanceDetailRow>> GetAllowancesDetailAsync(string stepCode)
    {
        const string sql = """
            SELECT allowance_id       AS AllowanceId,
                   effective_from     AS EffectiveFrom,
                   effective_to       AS EffectiveTo,
                   filing_status_code AS FilingStatusCode,
                   annual_amount      AS AnnualAmount
            FROM   tax_allowances
            WHERE  step_code = @StepCode
            ORDER  BY effective_from DESC
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<AllowanceDetailRow>(sql, new { StepCode = stepCode })).AsList();
    }

    public async Task<IReadOnlyList<TieredBracketDetailRow>> GetTieredBracketsDetailAsync(string stepCode)
    {
        const string sql = """
            SELECT tiered_bracket_id  AS TieredBracketId,
                   effective_from     AS EffectiveFrom,
                   effective_to       AS EffectiveTo,
                   lower_limit        AS LowerLimit,
                   upper_limit        AS UpperLimit,
                   rate               AS Rate,
                   period_cap_amount  AS PeriodCap,
                   annual_cap_amount  AS AnnualCap
            FROM   tax_tiered_brackets
            WHERE  step_code = @StepCode
            ORDER  BY effective_from DESC, lower_limit
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<TieredBracketDetailRow>(sql, new { StepCode = stepCode })).AsList();
    }

    public async Task<IReadOnlyList<CreditDetailRow>> GetCreditsDetailAsync(string stepCode)
    {
        const string sql = """
            SELECT credit_id       AS CreditId,
                   effective_from  AS EffectiveFrom,
                   effective_to    AS EffectiveTo,
                   annual_amount   AS AnnualAmount,
                   credit_rate     AS CreditRate,
                   is_refundable   AS IsRefundable
            FROM   tax_credits
            WHERE  step_code = @StepCode
            ORDER  BY effective_from DESC
            """;
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<CreditDetailRow>(sql, new { StepCode = stepCode })).AsList();
    }

    public async Task<IReadOnlySet<string>> GetExpiringStepCodesAsync(DateOnly asOf, int daysAhead = 120)
    {
        var threshold = asOf.AddDays(daysAhead);
        const string sql = """
            SELECT DISTINCT step_code FROM (
                SELECT f.step_code FROM tax_flat_rates f
                WHERE  f.effective_to IS NOT NULL
                  AND  f.effective_to >= @Today
                  AND  f.effective_to <= @Threshold
                  AND  NOT EXISTS (SELECT 1 FROM tax_flat_rates f2
                                   WHERE f2.step_code = f.step_code AND f2.effective_to IS NULL)
                UNION ALL
                SELECT b.step_code FROM tax_brackets b
                WHERE  b.effective_to IS NOT NULL
                  AND  b.effective_to >= @Today
                  AND  b.effective_to <= @Threshold
                  AND  NOT EXISTS (SELECT 1 FROM tax_brackets b2
                                   WHERE b2.step_code = b.step_code AND b2.effective_to IS NULL)
                UNION ALL
                SELECT a.step_code FROM tax_allowances a
                WHERE  a.effective_to IS NOT NULL
                  AND  a.effective_to >= @Today
                  AND  a.effective_to <= @Threshold
                  AND  NOT EXISTS (SELECT 1 FROM tax_allowances a2
                                   WHERE a2.step_code = a.step_code AND a2.effective_to IS NULL)
                UNION ALL
                SELECT t.step_code FROM tax_tiered_brackets t
                WHERE  t.effective_to IS NOT NULL
                  AND  t.effective_to >= @Today
                  AND  t.effective_to <= @Threshold
                  AND  NOT EXISTS (SELECT 1 FROM tax_tiered_brackets t2
                                   WHERE t2.step_code = t.step_code AND t2.effective_to IS NULL)
                UNION ALL
                SELECT c.step_code FROM tax_credits c
                WHERE  c.effective_to IS NOT NULL
                  AND  c.effective_to >= @Today
                  AND  c.effective_to <= @Threshold
                  AND  NOT EXISTS (SELECT 1 FROM tax_credits c2
                                   WHERE c2.step_code = c.step_code AND c2.effective_to IS NULL)
            ) expiring
            """;
        using var conn = _db.CreateConnection();
        var codes = await conn.QueryAsync<string>(sql, new { Today = asOf, Threshold = threshold });
        return codes.ToHashSet();
    }

    // ── Rate admin writes — flat rate ─────────────────────────────────

    public async Task InsertFlatRateAsync(
        string stepCode, DateOnly effectiveFrom, decimal rate,
        decimal? wageBase, decimal? periodCap, decimal? annualCap, string actor)
    {
        var effFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue);
        using var conn = _db.CreateConnection();

        var openCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_flat_rates WHERE step_code = @StepCode AND effective_to IS NULL",
            new { StepCode = stepCode });
        if (openCount > 0)
            throw new InvalidOperationException("Close the current open rate row before inserting a new one.");

        var conflictCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_flat_rates WHERE step_code = @StepCode AND effective_from >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (conflictCount > 0)
            throw new InvalidOperationException("New effective_from must be later than all existing rows.");

        var overlapCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_flat_rates WHERE step_code = @StepCode AND effective_to IS NOT NULL AND effective_to >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (overlapCount > 0)
            throw new InvalidOperationException("New effective_from overlaps a closed row — start the new rate the day after that row's close date.");

        const string sql = """
            INSERT INTO tax_flat_rates
              (step_code, effective_from, rate, wage_base, period_cap_amount, annual_cap_amount)
            VALUES
              (@StepCode, @EffectiveFrom, @Rate, @WageBase, @PeriodCap, @AnnualCap)
            """;
        await conn.ExecuteAsync(sql, new
        {
            StepCode      = stepCode,
            EffectiveFrom = effFrom,
            Rate          = rate,
            WageBase      = wageBase,
            PeriodCap     = periodCap,
            AnnualCap     = annualCap
        });
    }

    public async Task CloseFlatRateAsync(int flatRateId, DateOnly effectiveTo, string actor)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE tax_flat_rates SET effective_to = @EffectiveTo WHERE flat_rate_id = @Id",
            new { EffectiveTo = effectiveTo.ToDateTime(TimeOnly.MinValue), Id = flatRateId });
    }

    // ── Rate admin writes — brackets ──────────────────────────────────

    public async Task InsertBracketSetAsync(
        string stepCode, DateOnly effectiveFrom,
        IReadOnlyList<BracketInput> rows, string actor)
    {
        if (rows.Count == 0) throw new InvalidOperationException("At least one bracket row is required.");
        var effFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue);
        using var conn = _db.CreateConnection();

        var openCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_brackets WHERE step_code = @StepCode AND effective_to IS NULL",
            new { StepCode = stepCode });
        if (openCount > 0)
            throw new InvalidOperationException("Close the current open bracket set before inserting a new one.");

        var conflictCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_brackets WHERE step_code = @StepCode AND effective_from >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (conflictCount > 0)
            throw new InvalidOperationException("New effective_from must be later than all existing rows.");

        var overlapCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_brackets WHERE step_code = @StepCode AND effective_to IS NOT NULL AND effective_to >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (overlapCount > 0)
            throw new InvalidOperationException("New effective_from overlaps a closed row — start the new bracket set the day after that row's close date.");

        const string sql = """
            INSERT INTO tax_brackets
              (step_code, filing_status_code, effective_from, lower_limit, upper_limit, rate)
            VALUES
              (@StepCode, @FilingStatusCode, @EffectiveFrom, @LowerLimit, @UpperLimit, @Rate)
            """;
        using var tx = conn.BeginTransaction();
        foreach (var r in rows)
            await conn.ExecuteAsync(sql, new
            {
                StepCode          = stepCode,
                FilingStatusCode  = r.FilingStatusCode,
                EffectiveFrom     = effFrom,
                LowerLimit        = r.LowerLimit,
                UpperLimit        = r.UpperLimit,
                Rate              = r.Rate
            }, tx);
        tx.Commit();
    }

    public async Task CloseBracketSetAsync(
        string stepCode, DateOnly effectiveFrom, DateOnly effectiveTo, string actor)
    {
        var effFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue);
        var effTo   = effectiveTo.ToDateTime(TimeOnly.MinValue);
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE tax_brackets SET effective_to = @EffectiveTo WHERE step_code = @StepCode AND effective_from = @EffectiveFrom AND effective_to IS NULL",
            new { StepCode = stepCode, EffectiveFrom = effFrom, EffectiveTo = effTo });
    }

    // ── Rate admin writes — allowances ────────────────────────────────

    public async Task InsertAllowanceAsync(
        string stepCode, DateOnly effectiveFrom,
        string? filingStatusCode, decimal annualAmount, string actor)
    {
        var effFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue);
        using var conn = _db.CreateConnection();

        var openCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_allowances WHERE step_code = @StepCode AND effective_to IS NULL",
            new { StepCode = stepCode });
        if (openCount > 0)
            throw new InvalidOperationException("Close the current open allowance row before inserting a new one.");

        var conflictCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_allowances WHERE step_code = @StepCode AND effective_from >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (conflictCount > 0)
            throw new InvalidOperationException("New effective_from must be later than all existing rows.");

        var overlapCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_allowances WHERE step_code = @StepCode AND effective_to IS NOT NULL AND effective_to >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (overlapCount > 0)
            throw new InvalidOperationException("New effective_from overlaps a closed row — start the new allowance the day after that row's close date.");

        await conn.ExecuteAsync(
            "INSERT INTO tax_allowances (step_code, filing_status_code, effective_from, annual_amount) VALUES (@StepCode, @FilingStatusCode, @EffectiveFrom, @AnnualAmount)",
            new { StepCode = stepCode, FilingStatusCode = filingStatusCode, EffectiveFrom = effFrom, AnnualAmount = annualAmount });
    }

    public async Task CloseAllowanceAsync(int allowanceId, DateOnly effectiveTo, string actor)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE tax_allowances SET effective_to = @EffectiveTo WHERE allowance_id = @Id",
            new { EffectiveTo = effectiveTo.ToDateTime(TimeOnly.MinValue), Id = allowanceId });
    }

    // ── Rate admin writes — tiered brackets ───────────────────────────

    public async Task InsertTieredBracketSetAsync(
        string stepCode, DateOnly effectiveFrom,
        IReadOnlyList<TieredBracketInput> rows, string actor)
    {
        if (rows.Count == 0) throw new InvalidOperationException("At least one tier row is required.");
        var effFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue);
        using var conn = _db.CreateConnection();

        var openCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_tiered_brackets WHERE step_code = @StepCode AND effective_to IS NULL",
            new { StepCode = stepCode });
        if (openCount > 0)
            throw new InvalidOperationException("Close the current open tiered bracket set before inserting a new one.");

        var conflictCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_tiered_brackets WHERE step_code = @StepCode AND effective_from >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (conflictCount > 0)
            throw new InvalidOperationException("New effective_from must be later than all existing rows.");

        var overlapCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_tiered_brackets WHERE step_code = @StepCode AND effective_to IS NOT NULL AND effective_to >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (overlapCount > 0)
            throw new InvalidOperationException("New effective_from overlaps a closed row — start the new tier set the day after that row's close date.");

        const string sql = """
            INSERT INTO tax_tiered_brackets
              (step_code, effective_from, lower_limit, upper_limit, rate, period_cap_amount, annual_cap_amount)
            VALUES
              (@StepCode, @EffectiveFrom, @LowerLimit, @UpperLimit, @Rate, @PeriodCap, @AnnualCap)
            """;
        using var tx = conn.BeginTransaction();
        foreach (var r in rows)
            await conn.ExecuteAsync(sql, new
            {
                StepCode      = stepCode,
                EffectiveFrom = effFrom,
                LowerLimit    = r.LowerLimit,
                UpperLimit    = r.UpperLimit,
                Rate          = r.Rate,
                PeriodCap     = r.PeriodCap,
                AnnualCap     = r.AnnualCap
            }, tx);
        tx.Commit();
    }

    public async Task CloseTieredBracketSetAsync(
        string stepCode, DateOnly effectiveFrom, DateOnly effectiveTo, string actor)
    {
        var effFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue);
        var effTo   = effectiveTo.ToDateTime(TimeOnly.MinValue);
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE tax_tiered_brackets SET effective_to = @EffectiveTo WHERE step_code = @StepCode AND effective_from = @EffectiveFrom AND effective_to IS NULL",
            new { StepCode = stepCode, EffectiveFrom = effFrom, EffectiveTo = effTo });
    }

    // ── Rate admin writes — credits ───────────────────────────────────

    public async Task InsertCreditAsync(
        string stepCode, DateOnly effectiveFrom,
        decimal annualAmount, decimal creditRate, bool isRefundable, string actor)
    {
        var effFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue);
        using var conn = _db.CreateConnection();

        var openCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_credits WHERE step_code = @StepCode AND effective_to IS NULL",
            new { StepCode = stepCode });
        if (openCount > 0)
            throw new InvalidOperationException("Close the current open credit row before inserting a new one.");

        var conflictCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_credits WHERE step_code = @StepCode AND effective_from >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (conflictCount > 0)
            throw new InvalidOperationException("New effective_from must be later than all existing rows.");

        var overlapCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tax_credits WHERE step_code = @StepCode AND effective_to IS NOT NULL AND effective_to >= @NewFrom",
            new { StepCode = stepCode, NewFrom = effectiveFrom });
        if (overlapCount > 0)
            throw new InvalidOperationException("New effective_from overlaps a closed row — start the new credit the day after that row's close date.");

        await conn.ExecuteAsync(
            "INSERT INTO tax_credits (step_code, effective_from, annual_amount, credit_rate, is_refundable) VALUES (@StepCode, @EffectiveFrom, @AnnualAmount, @CreditRate, @IsRefundable)",
            new { StepCode = stepCode, EffectiveFrom = effFrom, AnnualAmount = annualAmount, CreditRate = creditRate, IsRefundable = isRefundable });
    }

    public async Task CloseCreditAsync(int creditId, DateOnly effectiveTo, string actor)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE tax_credits SET effective_to = @EffectiveTo WHERE credit_id = @Id",
            new { EffectiveTo = effectiveTo.ToDateTime(TimeOnly.MinValue), Id = creditId });
    }

    private static string StatusToAction(string priorStatus, string newStatus) =>
        (priorStatus, newStatus) switch
        {
            (_, "PENDING_REVIEW")          => "SUBMITTED_FOR_REVIEW",
            (_, "APPROVED")                => "APPROVED",
            (_, "ACTIVE")                  => "ACTIVATED",
            (_, "ARCHIVED")                => "ARCHIVED",
            ("PENDING_REVIEW", "DRAFT")    => "REJECTED",
            _                              => "EDITED"
        };
}
