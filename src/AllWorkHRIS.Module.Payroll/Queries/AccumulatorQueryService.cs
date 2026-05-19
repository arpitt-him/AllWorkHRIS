using AllWorkHRIS.Core.Data;
using Dapper;

namespace AllWorkHRIS.Module.Payroll.Queries;

// ── Display DTOs ─────────────────────────────────────────────────────────────

public sealed record AccumulatorFamilyListItem(
    int    FamilyId,
    string Code,
    string Label,
    bool   IsTaxRelated,
    int    SortOrder
);

public sealed record AccumulatorYearTab(
    int    Year,
    string Label
);

public sealed record AccumulatorEmployeeBalanceRow(
    Guid     EmploymentId,
    string   EmployeeNumber,
    string   EmployeeName,
    Guid     AccumulatorDefinitionId,
    string   DefinitionCode,
    string   DefinitionName,
    decimal  YtdBalance,
    decimal? CapAmount,
    DateOnly LastUpdated
);

public sealed record AccumulatorImpactRow(
    Guid     ImpactId,
    Guid     RunId,
    string   RunLabel,
    DateOnly RunDate,
    string   ImpactType,
    decimal  DeltaAmount,
    decimal  RunningBalance,
    bool     IsRetroactive,
    bool     IsReversal,
    bool     IsCorrection,
    string?  Notes
);

public sealed record AccumulatorResetHistoryRow(
    int      PeriodYear,
    decimal  ClosingBalance,
    DateOnly LastUpdatedDate
);

public sealed record AccumulatorEmployeeSearchResult(
    Guid   EmploymentId,
    string EmployeeNumber,
    string EmployeeName
);

public sealed record AccumulatorEmployeeSummaryRow(
    int      FamilyId,
    string   FamilyLabel,
    string   ResetType,
    int      CurrentYear,
    decimal  YtdBalance,
    decimal? CapAmount,
    Guid     AccumulatorDefinitionId
);

public sealed record AccumulatorFamilyMeta(
    string   RollupType,
    decimal? CapAmount,
    string   ResetType,
    int?     PlanYearStartMonth,
    int?     PlanYearStartDay
);

// ── Service ──────────────────────────────────────────────────────────────────

public sealed class AccumulatorQueryService
{
    private readonly IConnectionFactory _connectionFactory;

    public AccumulatorQueryService(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    /// Returns distinct accumulator families that have balance records for the entity.
    public async Task<IReadOnlyList<AccumulatorFamilyListItem>> GetFamiliesWithBalancesAsync(Guid legalEntityId)
    {
        const string sql = """
            SELECT DISTINCT af.id            AS FamilyId,
                            af.code          AS Code,
                            af.label         AS Label,
                            af.is_tax_related AS IsTaxRelated,
                            af.sort_order    AS SortOrder
            FROM   accumulator_balance ab
            JOIN   lkp_accumulator_family af ON af.id = ab.accumulator_family_id
            WHERE  ab.participant_id IN (
                       SELECT employment_id FROM employment WHERE legal_entity_id = @LegalEntityId
                   )
               OR  ab.employer_id = @LegalEntityId
            ORDER BY af.sort_order, af.label
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<AccumulatorFamilyListItem>(sql,
            new { LegalEntityId = legalEntityId })).ToList();
    }

    /// Returns definition-level metadata for a family (rollup type, cap, plan year dates).
    public async Task<AccumulatorFamilyMeta?> GetFamilyMetaAsync(int familyId, Guid legalEntityId)
    {
        const string sql = """
            SELECT ad.rollup_type,
                   ad.cap_amount,
                   ad.reset_type,
                   ad.plan_year_start_month,
                   ad.plan_year_start_day
            FROM   accumulator_definition ad
            JOIN   accumulator_balance ab ON ab.accumulator_definition_id = ad.accumulator_definition_id
            WHERE  ad.accumulator_family_id = @FamilyId
              AND (ab.participant_id IN (
                       SELECT employment_id FROM employment WHERE legal_entity_id = @LegalEntityId
                   )
               OR  ab.employer_id = @LegalEntityId)
            FETCH FIRST 1 ROWS ONLY
            """;
        using var conn = _connectionFactory.CreateConnection();
        var rows = (await conn.QueryAsync(sql,
            new { FamilyId = familyId, LegalEntityId = legalEntityId })).ToList();
        if (rows.Count == 0) return null;
        var r = rows[0];
        return new AccumulatorFamilyMeta(
            (string)r.rollup_type,
            r.cap_amount as decimal?,
            (string)r.reset_type,
            r.plan_year_start_month as int?,
            r.plan_year_start_day   as int?
        );
    }

    /// Returns distinct calendar years that have balance records for the family+entity.
    /// For PLAN_YEAR families the tab label is formatted as "PY YYYY–YYYY".
    public async Task<IReadOnlyList<AccumulatorYearTab>> GetAvailableYearsAsync(
        int familyId, Guid legalEntityId, string resetType, int? planYearStartMonth)
    {
        const string sql = """
            SELECT DISTINCT pp.period_year
            FROM   accumulator_balance ab
            JOIN   payroll_period pp ON pp.period_id = ab.calendar_context_id
            WHERE  ab.accumulator_family_id = @FamilyId
              AND (ab.participant_id IN (
                       SELECT employment_id FROM employment WHERE legal_entity_id = @LegalEntityId
                   )
               OR  ab.employer_id = @LegalEntityId)
            ORDER BY pp.period_year DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        var years = (await conn.QueryAsync<int>(sql,
            new { FamilyId = familyId, LegalEntityId = legalEntityId })).ToList();
        return years.Select(y => new AccumulatorYearTab(y, BuildYearLabel(y, resetType, planYearStartMonth))).ToList();
    }

    private static string BuildYearLabel(int year, string resetType, int? planYearStartMonth)
    {
        if (resetType != "PLAN_YEAR") return year.ToString();
        if (planYearStartMonth is null or 1) return $"PY {year}";
        return $"PY {year}–{year + 1}";
    }

    /// Returns one balance row per employee for employee-scoped families.
    public async Task<IReadOnlyList<AccumulatorEmployeeBalanceRow>> GetEmployeeBalancesAsync(
        int familyId, Guid legalEntityId, int year)
    {
        const string sql = """
            SELECT e.employment_id,
                   e.employee_number,
                   p.legal_first_name || ' ' || p.legal_last_name AS employee_name,
                   ad.accumulator_definition_id,
                   ad.accumulator_code,
                   ad.accumulator_name,
                   ad.cap_amount,
                   SUM(ab.current_value)        AS ytd_balance,
                   MAX(ab.last_update_timestamp) AS last_updated
            FROM   accumulator_balance ab
            JOIN   accumulator_definition ad ON ad.accumulator_definition_id = ab.accumulator_definition_id
            JOIN   payroll_period pp          ON pp.period_id                = ab.calendar_context_id
            JOIN   employment e               ON e.employment_id             = ab.participant_id
            JOIN   person p                   ON p.person_id                 = e.person_id
            WHERE  ab.accumulator_family_id = @FamilyId
              AND  e.legal_entity_id        = @LegalEntityId
              AND  pp.period_year           = @Year
              AND  ab.participant_id IS NOT NULL
            GROUP BY e.employment_id, e.employee_number,
                     p.legal_first_name, p.legal_last_name,
                     ad.accumulator_definition_id, ad.accumulator_code, ad.accumulator_name
            ORDER BY e.employee_number
            """;
        using var conn = _connectionFactory.CreateConnection();
        var raws = (await conn.QueryAsync(sql,
            new { FamilyId = familyId, LegalEntityId = legalEntityId, Year = year })).ToList();

        return raws.Select(r => new AccumulatorEmployeeBalanceRow(
            (Guid)r.employment_id,
            (string)r.employee_number,
            (string)r.employee_name,
            (Guid)r.accumulator_definition_id,
            (string)r.accumulator_code,
            (string)r.accumulator_name,
            (decimal)r.ytd_balance,
            r.cap_amount as decimal?,
            ToDateOnly(r.last_updated)
        )).ToList();
    }

    /// Returns the impact trail for an employee+definition+year, with cumulative running balance.
    public async Task<IReadOnlyList<AccumulatorImpactRow>> GetImpactTrailAsync(
        Guid definitionId, Guid employmentId, int year)
    {
        const string sql = """
            SELECT ai.accumulator_impact_id AS impact_id,
                   ai.payroll_run_id        AS run_id,
                   pr.pay_date              AS run_date,
                   pr.run_description,
                   pp.period_number,
                   ai.delta_value,
                   ai.retroactive_flag,
                   ai.reversal_flag,
                   ai.correction_flag,
                   ai.notes
            FROM   accumulator_impact ai
            JOIN   payroll_run pr    ON pr.run_id    = ai.payroll_run_id
            JOIN   payroll_period pp ON pp.period_id = pr.period_id
            WHERE  ai.accumulator_definition_id = @DefinitionId
              AND  ai.employment_id             = @EmploymentId
              AND  pp.period_year               = @Year
            ORDER BY ai.impact_timestamp ASC
            """;
        using var conn = _connectionFactory.CreateConnection();
        var raws = (await conn.QueryAsync(sql,
            new { DefinitionId = definitionId, EmploymentId = employmentId, Year = year })).ToList();

        return BuildImpactRows(raws);
    }

    /// Returns closing balance per year for reset history display.
    public async Task<IReadOnlyList<AccumulatorResetHistoryRow>> GetResetHistoryAsync(int familyId, Guid legalEntityId)
    {
        const string sql = """
            SELECT pp.period_year,
                   SUM(ab.current_value)         AS closing_balance,
                   MAX(ab.last_update_timestamp)  AS last_updated
            FROM   accumulator_balance ab
            JOIN   payroll_period pp ON pp.period_id  = ab.calendar_context_id
            JOIN   employment e      ON e.employment_id = ab.participant_id
            WHERE  ab.accumulator_family_id = @FamilyId
              AND  e.legal_entity_id        = @LegalEntityId
              AND  ab.participant_id IS NOT NULL
            GROUP BY pp.period_year
            ORDER BY pp.period_year DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        var raws = (await conn.QueryAsync(sql,
            new { FamilyId = familyId, LegalEntityId = legalEntityId })).ToList();

        return raws.Select(r => new AccumulatorResetHistoryRow(
            (int)r.period_year,
            (decimal)r.closing_balance,
            ToDateOnly(r.last_updated)
        )).ToList();
    }

    /// Employee name/number search within a legal entity (max 20 results).
    public async Task<IReadOnlyList<AccumulatorEmployeeSearchResult>> SearchEmployeesAsync(
        Guid legalEntityId, string query)
    {
        const string sql = """
            SELECT e.employment_id   AS EmploymentId,
                   e.employee_number AS EmployeeNumber,
                   p.legal_first_name || ' ' || p.legal_last_name AS EmployeeName
            FROM   employment e
            JOIN   person p ON p.person_id = e.person_id
            WHERE  e.legal_entity_id = @LegalEntityId
              AND (LOWER(e.employee_number) LIKE LOWER(@Query)
               OR  LOWER(p.legal_first_name || ' ' || p.legal_last_name) LIKE LOWER(@Query))
            ORDER BY e.employee_number
            FETCH FIRST 20 ROWS ONLY
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<AccumulatorEmployeeSearchResult>(sql,
            new { LegalEntityId = legalEntityId, Query = $"%{query}%" })).ToList();
    }

    /// Returns all accumulator families with YTD balances for a specific employee.
    public async Task<IReadOnlyList<AccumulatorEmployeeSummaryRow>> GetEmployeeSummaryAsync(
        Guid employmentId, int year)
    {
        const string sql = """
            SELECT af.id                        AS family_id,
                   af.label                     AS family_label,
                   ad.reset_type,
                   pp.period_year               AS current_year,
                   SUM(ab.current_value)        AS ytd_balance,
                   ad.cap_amount,
                   ad.accumulator_definition_id
            FROM   accumulator_balance ab
            JOIN   accumulator_definition ad ON ad.accumulator_definition_id = ab.accumulator_definition_id
            JOIN   lkp_accumulator_family af ON af.id                        = ab.accumulator_family_id
            JOIN   payroll_period pp          ON pp.period_id                = ab.calendar_context_id
            WHERE  ab.participant_id = @EmploymentId
              AND  pp.period_year    = @Year
            GROUP BY af.id, af.label, af.sort_order, ad.reset_type, pp.period_year,
                     ad.accumulator_definition_id
            ORDER BY af.sort_order, af.label
            """;
        using var conn = _connectionFactory.CreateConnection();
        var raws = (await conn.QueryAsync(sql,
            new { EmploymentId = employmentId, Year = year })).ToList();

        return raws.Select(r => new AccumulatorEmployeeSummaryRow(
            (int)r.family_id,
            (string)r.family_label,
            (string)r.reset_type,
            (int)r.current_year,
            (decimal)r.ytd_balance,
            r.cap_amount as decimal?,
            (Guid)r.accumulator_definition_id
        )).ToList();
    }

    /// Returns the impact trail for an employee across all definitions in a family, for a given year.
    public async Task<IReadOnlyList<AccumulatorImpactRow>> GetEmployeeFamilyImpactAsync(
        Guid employmentId, int familyId, int year)
    {
        const string sql = """
            SELECT ai.accumulator_impact_id AS impact_id,
                   ai.payroll_run_id        AS run_id,
                   pr.pay_date              AS run_date,
                   pr.run_description,
                   pp.period_number,
                   ai.delta_value,
                   ai.retroactive_flag,
                   ai.reversal_flag,
                   ai.correction_flag,
                   ai.notes
            FROM   accumulator_impact ai
            JOIN   accumulator_definition ad ON ad.accumulator_definition_id = ai.accumulator_definition_id
            JOIN   payroll_run pr    ON pr.run_id    = ai.payroll_run_id
            JOIN   payroll_period pp ON pp.period_id = pr.period_id
            WHERE  ai.employment_id         = @EmploymentId
              AND  ad.accumulator_family_id = @FamilyId
              AND  pp.period_year           = @Year
            ORDER BY ai.impact_timestamp ASC
            """;
        using var conn = _connectionFactory.CreateConnection();
        var raws = (await conn.QueryAsync(sql,
            new { EmploymentId = employmentId, FamilyId = familyId, Year = year })).ToList();

        return BuildImpactRows(raws);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static IReadOnlyList<AccumulatorImpactRow> BuildImpactRows(IReadOnlyList<dynamic> raws)
    {
        decimal running = 0;
        var result = new List<AccumulatorImpactRow>(raws.Count);
        foreach (var r in raws)
        {
            decimal delta  = (decimal)r.delta_value;
            running += delta;

            bool isReversal   = (bool)r.reversal_flag;
            bool isCorrection = (bool)r.correction_flag;
            bool isRetro      = (bool)r.retroactive_flag;

            string type = isReversal   ? "REVERSAL"
                        : isCorrection ? "CORRECTION"
                        : "CONTRIBUTION";

            DateOnly runDate = ToDateOnly(r.run_date);
            int periodNum    = (int)r.period_number;
            string? desc     = r.run_description as string;
            string runLabel  = !string.IsNullOrEmpty(desc)
                ? $"{desc} ({runDate:MMM d})"
                : $"P{periodNum} — {runDate:MMM d, yyyy}";

            result.Add(new AccumulatorImpactRow(
                (Guid)r.impact_id,
                (Guid)r.run_id,
                runLabel,
                runDate,
                type,
                delta,
                running,
                isRetro,
                isReversal,
                isCorrection,
                r.notes as string
            ));
        }
        return result;
    }

    private static DateOnly ToDateOnly(object val) => val switch
    {
        DateOnly d  => d,
        DateTime dt => DateOnly.FromDateTime(dt.Date),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime.Date),
        _ => DateOnly.FromDateTime(Convert.ToDateTime(val).Date)
    };
}
