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
