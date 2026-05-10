using AllWorkHRIS.Core.Data;
using Dapper;

namespace AllWorkHRIS.Module.Payroll.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record PayRegisterRunOption(
    Guid     RunId,
    DateOnly PayDate,
    int      PeriodYear,
    int      PeriodNumber,
    string?  RunDescription,
    int      RunStatusId)
{
    public int    Year  => PayDate.Year;
    public int    Month => PayDate.Month;
    public string Label => RunDescription is { Length: > 0 } d
        ? $"{d} ({PayDate:MMM d})"
        : $"P{PeriodNumber} — {PayDate:MMM d, yyyy}";
    public string StatusLabel => RunStatusId switch
    {
        6 => "Approved", 7 => "Releasing", 8 => "Released", 9 => "Closed", _ => "—"
    };
}

public sealed record PayRegisterRunHeader(
    Guid     RunId,
    DateOnly PayDate,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    int      PeriodYear,
    int      PeriodNumber,
    string?  RunDescription,
    int      RunStatusId);

public sealed record PayRegisterCompanySummary
{
    public decimal TotalGrossPayroll            { get; init; }
    public decimal TotalEmployerTaxes           { get; init; }
    public decimal TotalEmployerBenefitContrib  { get; init; }
    public decimal TotalEmployerCost            { get; init; }
    public decimal TotalEmployeeTaxWithholdings { get; init; }
    public decimal TotalEmployeeBenefitDeducts  { get; init; }
    public decimal TotalNetPay                  { get; init; }
    public decimal TotalCashRequired            { get; init; }
    // Tax detail
    public decimal FederalIncomeTax  { get; init; }
    public decimal SocialSecurityEe  { get; init; }
    public decimal SocialSecurityEr  { get; init; }
    public decimal MedicareEe        { get; init; }
    public decimal MedicareEr        { get; init; }
    public decimal Futa              { get; init; }
    public IReadOnlyList<(string Label, decimal Amount)> StateAndLocalTax { get; init; } = [];
    public IReadOnlyList<(string Label, decimal Amount)> SutaByState      { get; init; } = [];
    // Benefit detail
    public decimal TotalEmployeeDeductions    { get; init; }
    public decimal TotalEmployerContributions { get; init; }
    public decimal TotalRetirementDeductions  { get; init; }
    // Headcount
    public int EmployeesIncluded            { get; init; }
    public int EmployeesWithSupplementalPay { get; init; }
}

public sealed record PayRegisterOrgRow(
    Guid    GroupId,
    string  GroupName,
    int     Headcount,
    decimal GrossWages,
    decimal EmployeeTaxWithheld,
    decimal EmployerTaxCost,
    decimal EmployerBenefitCost,
    decimal NetPay,
    decimal TotalEmployerCost);

public sealed record PayRegisterEmployeeRow(
    Guid    EmployeePayrollResultId,
    Guid    EmploymentId,
    string  EmployeeNumber,
    string  EmployeeName,
    string  DepartmentName,
    Guid?   DivisionId,
    decimal Gross,
    decimal FederalTax,
    decimal SsEe,
    decimal MedicareEe,
    decimal StateTax,
    decimal LocalTax,
    decimal BenefitDeductions,
    decimal RetirementDeductions,
    decimal Garnishments,
    decimal NetPay,
    decimal EmployerSs,
    decimal EmployerMedicare,
    decimal EmployerBenefit);

public sealed record PayRegisterLineItem(
    string  Category,
    string  Code,
    string  Description,
    decimal Amount);

// Represents one discovered org-hierarchy level in a payroll run (e.g. DIVISION, DEPARTMENT, TEAM).
public sealed record OrgDimension(string Code, string Label, int SortOrder);

// Country-level column label config for the Employee Detail table.
// LabelLocalTax == null means the local-tax column is hidden for that country.
public sealed record PayRegisterColumnLabels(
    string  LabelFederalTax,
    string  LabelPensionEe,
    string  LabelInsuranceEe,
    string  LabelStateTax,
    string? LabelLocalTax,
    string  LabelErPension,
    string  LabelErInsurance)
{
    public static PayRegisterColumnLabels Default => new(
        "Fed Tax", "SS (EE)", "Medicare (EE)", "State Tax", "Local Tax", "SS (ER)", "Medicare (ER)");
}

// ── Query service ─────────────────────────────────────────────────────────────

public sealed class PayRegisterQueryService
{
    private readonly IConnectionFactory _connectionFactory;

    public PayRegisterQueryService(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    // ── Navigator ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PayRegisterRunOption>> GetRunNavigatorAsync(Guid legalEntityId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayRegisterRunOption>(
            """
            SELECT pr.run_id, pr.pay_date, pp.period_year, pp.period_number,
                   pr.run_description, pr.run_status_id
            FROM   payroll_run pr
            JOIN   payroll_context pc ON pc.payroll_context_id = pr.payroll_context_id
            JOIN   payroll_period  pp ON pp.period_id          = pr.period_id
            WHERE  pc.legal_entity_id = @LegalEntityId
              AND  pr.run_status_id IN (6, 7, 8, 9)
            ORDER  BY pr.pay_date DESC
            """,
            new { LegalEntityId = legalEntityId })).ToList();
    }

    public async Task<PayRegisterRunHeader?> GetRunHeaderAsync(Guid runId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PayRegisterRunHeader>(
            """
            SELECT pr.run_id, pr.pay_date,
                   pp.period_start_date, pp.period_end_date, pp.period_year, pp.period_number,
                   pr.run_description, pr.run_status_id
            FROM   payroll_run pr
            JOIN   payroll_period pp ON pp.period_id = pr.period_id
            WHERE  pr.run_id = @RunId
            """,
            new { RunId = runId });
    }

    public async Task<PayRegisterColumnLabels> GetColumnLabelsAsync(Guid runId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<PayRegisterColumnLabels>(
            """
            SELECT COALESCE(c.label_federal_tax,  'Fed Tax')       AS label_federal_tax,
                   COALESCE(c.label_pension_ee,   'SS (EE)')        AS label_pension_ee,
                   COALESCE(c.label_insurance_ee, 'Medicare (EE)')  AS label_insurance_ee,
                   COALESCE(c.label_state_tax,    'State Tax')      AS label_state_tax,
                   c.label_local_tax                                AS label_local_tax,
                   COALESCE(c.label_er_pension,   'SS (ER)')        AS label_er_pension,
                   COALESCE(c.label_er_insurance, 'Medicare (ER)')  AS label_er_insurance
            FROM   payroll_run         pr
            JOIN   payroll_context     pc ON pc.payroll_context_id = pr.payroll_context_id
            JOIN   org_unit            ou ON ou.org_unit_id        = pc.legal_entity_id
            LEFT   JOIN lkp_payroll_register_columns c ON c.country_code = ou.country_code
            WHERE  pr.run_id = @RunId
            """,
            new { RunId = runId });
        return result ?? PayRegisterColumnLabels.Default;
    }

    // ── Company Summary ──────────────────────────────────────────────────────

    public async Task<PayRegisterCompanySummary> GetCompanySummaryAsync(Guid runId)
    {
        using var conn = _connectionFactory.CreateConnection();

        var netAndCount = await conn.QueryFirstOrDefaultAsync<dynamic>(
            """
            SELECT COALESCE(SUM(r.net_pay_amount), 0) AS total_net,
                   COUNT(DISTINCT r.employment_id)    AS employee_count
            FROM   employee_payroll_result r
            WHERE  r.payroll_run_id = @RunId
            """, new { RunId = runId });

        var grossTotal = await conn.QueryFirstOrDefaultAsync<decimal>(
            """
            SELECT COALESCE(SUM(el.calculated_amount), 0)
            FROM   earnings_result_line el
            JOIN   employee_payroll_result r ON r.employee_payroll_result_id = el.employee_payroll_result_id
            WHERE  r.payroll_run_id = @RunId
            """, new { RunId = runId });

        var taxRows = (await conn.QueryAsync<dynamic>(
            """
            SELECT tl.tax_code, tl.employer_flag,
                   COALESCE(cs.calculation_category, '') AS calculation_category,
                   COALESCE(cs.step_name, tl.tax_code)  AS step_name,
                   COALESCE(SUM(tl.calculated_amount), 0) AS amount
            FROM   tax_result_line tl
            JOIN   employee_payroll_result r ON r.employee_payroll_result_id = tl.employee_payroll_result_id
            LEFT   JOIN payroll_calculation_steps cs ON cs.step_code = tl.tax_code AND cs.is_active = TRUE
            WHERE  r.payroll_run_id = @RunId
            GROUP  BY tl.tax_code, tl.employer_flag, cs.calculation_category, cs.step_name
            """, new { RunId = runId })).ToList();

        var deductionRows = (await conn.QueryAsync<dynamic>(
            """
            SELECT dl.deduction_code, COALESCE(SUM(dl.calculated_amount), 0) AS amount
            FROM   deduction_result_line dl
            JOIN   employee_payroll_result r ON r.employee_payroll_result_id = dl.employee_payroll_result_id
            WHERE  r.payroll_run_id = @RunId
            GROUP  BY dl.deduction_code
            """, new { RunId = runId })).ToList();

        var erContrib = await conn.QueryFirstOrDefaultAsync<decimal>(
            """
            SELECT COALESCE(SUM(ecl.calculated_amount), 0)
            FROM   employer_contribution_result_line ecl
            JOIN   employee_payroll_result r ON r.employee_payroll_result_id = ecl.employee_payroll_result_id
            WHERE  r.payroll_run_id = @RunId
            """, new { RunId = runId });

        var suppCount = await conn.QueryFirstOrDefaultAsync<long>(
            """
            SELECT COUNT(DISTINCT r.employment_id)
            FROM   earnings_result_line el
            JOIN   employee_payroll_result r ON r.employee_payroll_result_id = el.employee_payroll_result_id
            WHERE  r.payroll_run_id = @RunId
              AND  UPPER(el.earnings_code) LIKE '%SUPPLEMENT%'
            """, new { RunId = runId });

        // Pivot tax rows
        decimal fedTax = 0, ssEe = 0, ssEr = 0, medEe = 0, medEr = 0, futa = 0;
        decimal totalErTax = 0, totalEeTax = 0;
        var stateLocalRows = new List<(string Label, decimal Amount)>();
        var sutaRows        = new List<(string Label, decimal Amount)>();

        foreach (var row in taxRows)
        {
            string  code     = (string)row.tax_code;
            bool    er       = (bool)row.employer_flag;
            decimal amt      = (decimal)row.amount;
            string  upper    = code.ToUpperInvariant();
            string  category = ((string)row.calculation_category).ToUpperInvariant();
            string  label    = (string)row.step_name;

            if (er) totalErTax += amt; else totalEeTax += amt;

            switch (category)
            {
                case "SOCIAL_INSURANCE":
                    if (upper.Contains("MEDICARE"))
                    { if (er) medEr += amt; else medEe += amt; }
                    else  // SS, CPP, NIS, EI, etc.
                    { if (er) ssEr += amt; else ssEe += amt; }
                    break;
                case "INCOME_TAX":
                    if (!er) fedTax += amt;
                    break;
                case "SUBNATIONAL_TAX":
                case "DERIVED_TAX":
                    if (!er && amt != 0) stateLocalRows.Add((label, amt));
                    break;
                default:
                    if (upper.Contains("FUTA") && amt != 0)      futa += amt;
                    else if (upper.Contains("SUTA") && amt != 0) sutaRows.Add((label, amt));
                    break;
            }
        }

        // Pivot deduction rows
        decimal totalDeducts = 0, retirement = 0, garnishments = 0;
        foreach (var row in deductionRows)
        {
            string  code  = ((string)row.deduction_code).ToUpperInvariant();
            decimal amt   = (decimal)row.amount;
            totalDeducts += amt;
            if (code.Contains("RETIRE") || code.Contains("401") || code.Contains("403")) retirement   += amt;
            if (code.Contains("GARNISH"))                                                  garnishments += amt;
        }

        decimal totalNet     = (decimal)netAndCount!.total_net;
        int     headcount    = (int)(long)netAndCount!.employee_count;

        return new PayRegisterCompanySummary
        {
            TotalGrossPayroll            = grossTotal,
            TotalEmployerTaxes           = totalErTax,
            TotalEmployerBenefitContrib  = erContrib,
            TotalEmployerCost            = grossTotal + totalErTax + erContrib,
            TotalEmployeeTaxWithholdings = totalEeTax,
            TotalEmployeeBenefitDeducts  = totalDeducts,
            TotalNetPay                  = totalNet,
            TotalCashRequired            = totalNet + totalErTax + erContrib,
            FederalIncomeTax             = fedTax,
            SocialSecurityEe             = ssEe,
            SocialSecurityEr             = ssEr,
            MedicareEe                   = medEe,
            MedicareEr                   = medEr,
            Futa                         = futa,
            StateAndLocalTax             = stateLocalRows,
            SutaByState                  = sutaRows,
            TotalEmployeeDeductions      = totalDeducts,
            TotalEmployerContributions   = erContrib,
            TotalRetirementDeductions    = retirement,
            EmployeesIncluded            = headcount,
            EmployeesWithSupplementalPay = (int)suppCount,
        };
    }

    // ── Org Rollup ───────────────────────────────────────────────────────────

    // Returns all org-hierarchy levels present in this run's org chains (e.g. DIVISION, DEPARTMENT, TEAM).
    // Uses a recursive CTE to walk up from each employee's assigned unit to the root,
    // collecting every distinct type code encountered.  LEGAL_ENTITY and COMPANY are excluded.
    public async Task<IReadOnlyList<OrgDimension>> GetAvailableOrgDimensionsAsync(Guid runId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(
            """
            WITH RECURSIVE
            leaf_units AS (
                SELECT DISTINCT ou.org_unit_id, ou.parent_org_unit_id
                FROM   employee_payroll_result r
                JOIN   employment e ON e.employment_id = r.employment_id
                LEFT   JOIN assignment a
                       ON  a.employment_id        = e.employment_id
                       AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                       AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')
                JOIN   org_unit ou ON ou.org_unit_id = a.department_id
                WHERE  r.payroll_run_id = @RunId
            ),
            org_walk(org_unit_id, parent_org_unit_id, depth) AS (
                SELECT org_unit_id, parent_org_unit_id, 0
                FROM   leaf_units
                UNION ALL
                SELECT p.org_unit_id, p.parent_org_unit_id, ow.depth + 1
                FROM   org_unit p
                JOIN   lkp_org_unit_type pt ON pt.id = p.org_unit_type_id
                JOIN   org_walk ow ON p.org_unit_id = ow.parent_org_unit_id
                WHERE  ow.parent_org_unit_id IS NOT NULL
                  AND  pt.code NOT IN ('LEGAL_ENTITY', 'COMPANY')
                  AND  ow.depth < 10
            )
            SELECT DISTINCT t.code, t.label, t.sort_order
            FROM   org_walk ow
            JOIN   org_unit ou ON ou.org_unit_id = ow.org_unit_id
            JOIN   lkp_org_unit_type t ON t.id = ou.org_unit_type_id
            WHERE  t.code NOT IN ('LEGAL_ENTITY', 'COMPANY')
            ORDER  BY t.sort_order
            """,
            new { RunId = runId });
        return rows.Select(r => new OrgDimension((string)r.code, (string)r.label, (int)r.sort_order)).ToList();
    }

    // Rolls up payroll totals by any org-hierarchy type (DIVISION, DEPARTMENT, TEAM, etc.) using a
    // recursive CTE that walks up each employee's org chain until it finds the target type.
    // Location and Job are non-hierarchy dimensions handled via their own join paths.
    public async Task<IReadOnlyList<PayRegisterOrgRow>> GetOrgRollupAsync(Guid runId, string dimension)
    {
        using var conn = _connectionFactory.CreateConnection();

        var dim = dimension.ToUpperInvariant();

        if (dim == "LOCATION" || dim == "JOB")
        {
            string groupIdCol    = dim == "LOCATION" ? "l.org_unit_id"                        : "j.job_id";
            string groupNameExpr = dim == "LOCATION" ? "COALESCE(l.org_unit_name,'Unassigned')" : "COALESCE(j.job_title,'Unassigned')";
            string groupJoin     = dim == "LOCATION" ? "LEFT JOIN org_unit l ON l.org_unit_id = a.location_id"
                                                      : "LEFT JOIN job j ON j.job_id = a.job_id";
            var flatSql = $"""
                WITH er_tax AS (
                    SELECT r.employment_id, COALESCE(SUM(tl.calculated_amount),0) AS employer_tax_total
                    FROM   tax_result_line tl
                    JOIN   employee_payroll_result r ON r.employee_payroll_result_id = tl.employee_payroll_result_id
                    WHERE  r.payroll_run_id = @RunId AND tl.employer_flag = TRUE
                    GROUP  BY r.employment_id
                ),
                er_benefit AS (
                    SELECT r.employment_id, COALESCE(SUM(ecl.calculated_amount),0) AS employer_benefit_total
                    FROM   employer_contribution_result_line ecl
                    JOIN   employee_payroll_result r ON r.employee_payroll_result_id = ecl.employee_payroll_result_id
                    WHERE  r.payroll_run_id = @RunId
                    GROUP  BY r.employment_id
                ),
                base AS (
                    SELECT {groupIdCol} AS grp_id, {groupNameExpr} AS grp_name,
                           r.employment_id, r.gross_pay_amount, r.total_employee_tax_amount,
                           r.net_pay_amount,
                           COALESCE(et.employer_tax_total,0)    AS employer_tax_total,
                           COALESCE(eb.employer_benefit_total,0) AS employer_benefit_total
                    FROM   employee_payroll_result r
                    JOIN   employment e ON e.employment_id = r.employment_id
                    LEFT   JOIN assignment a
                           ON  a.employment_id        = e.employment_id
                           AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                           AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')
                    {groupJoin}
                    LEFT   JOIN er_tax    et ON et.employment_id = r.employment_id
                    LEFT   JOIN er_benefit eb ON eb.employment_id = r.employment_id
                    WHERE  r.payroll_run_id = @RunId
                )
                SELECT grp_id, grp_name,
                       COUNT(DISTINCT employment_id)                         AS headcount,
                       COALESCE(SUM(gross_pay_amount),0)                     AS gross_wages,
                       COALESCE(SUM(total_employee_tax_amount),0)            AS employee_tax_withheld,
                       COALESCE(SUM(employer_tax_total),0)                   AS employer_tax_cost,
                       COALESCE(SUM(employer_benefit_total),0)               AS employer_benefit_cost,
                       COALESCE(SUM(net_pay_amount),0)                       AS net_pay
                FROM   base
                GROUP  BY grp_id, grp_name
                ORDER  BY grp_name
                """;
            return MapOrgRows(await conn.QueryAsync<dynamic>(flatSql, new { RunId = runId }));
        }

        // Org-hierarchy dimension: walk each employee's assignment up the tree until we reach
        // an ancestor of the target type, then aggregate.
        var recSql =
            """
            WITH RECURSIVE
            er_tax AS (
                SELECT r.employment_id, COALESCE(SUM(tl.calculated_amount),0) AS employer_tax_total
                FROM   tax_result_line tl
                JOIN   employee_payroll_result r ON r.employee_payroll_result_id = tl.employee_payroll_result_id
                WHERE  r.payroll_run_id = @RunId AND tl.employer_flag = TRUE
                GROUP  BY r.employment_id
            ),
            er_benefit AS (
                SELECT r.employment_id, COALESCE(SUM(ecl.calculated_amount),0) AS employer_benefit_total
                FROM   employer_contribution_result_line ecl
                JOIN   employee_payroll_result r ON r.employee_payroll_result_id = ecl.employee_payroll_result_id
                WHERE  r.payroll_run_id = @RunId
                GROUP  BY r.employment_id
            ),
            org_walk(employment_id, org_unit_id, org_unit_name, parent_org_unit_id, type_code, depth) AS (
                SELECT r.employment_id,
                       ou.org_unit_id, ou.org_unit_name, ou.parent_org_unit_id,
                       COALESCE(t.code,'') AS type_code, 0
                FROM   employee_payroll_result r
                JOIN   employment e ON e.employment_id = r.employment_id
                LEFT   JOIN assignment a
                       ON  a.employment_id        = e.employment_id
                       AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                       AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')
                LEFT   JOIN org_unit ou ON ou.org_unit_id = a.department_id
                LEFT   JOIN lkp_org_unit_type t ON t.id  = ou.org_unit_type_id
                WHERE  r.payroll_run_id = @RunId
                UNION ALL
                SELECT ow.employment_id,
                       p.org_unit_id, p.org_unit_name, p.parent_org_unit_id,
                       pt.code, ow.depth + 1
                FROM   org_walk ow
                JOIN   org_unit p ON p.org_unit_id = ow.parent_org_unit_id
                JOIN   lkp_org_unit_type pt ON pt.id = p.org_unit_type_id
                WHERE  ow.type_code <> @TargetTypeCode
                  AND  ow.parent_org_unit_id IS NOT NULL
                  AND  ow.depth < 10
            ),
            emp_group AS (
                SELECT DISTINCT employment_id, org_unit_id AS grp_id, org_unit_name AS grp_name
                FROM   org_walk
                WHERE  type_code = @TargetTypeCode
            ),
            base AS (
                SELECT COALESCE(eg.grp_id,   NULL)          AS grp_id,
                       COALESCE(eg.grp_name, 'Unassigned')  AS grp_name,
                       r.employment_id,
                       r.gross_pay_amount,
                       r.total_employee_tax_amount,
                       r.net_pay_amount,
                       COALESCE(et.employer_tax_total,    0) AS employer_tax_total,
                       COALESCE(eb.employer_benefit_total,0) AS employer_benefit_total
                FROM   employee_payroll_result r
                LEFT   JOIN emp_group  eg ON eg.employment_id = r.employment_id
                LEFT   JOIN er_tax     et ON et.employment_id = r.employment_id
                LEFT   JOIN er_benefit eb ON eb.employment_id = r.employment_id
                WHERE  r.payroll_run_id = @RunId
            )
            SELECT grp_id, grp_name,
                   COUNT(DISTINCT employment_id)                         AS headcount,
                   COALESCE(SUM(gross_pay_amount),0)                     AS gross_wages,
                   COALESCE(SUM(total_employee_tax_amount),0)            AS employee_tax_withheld,
                   COALESCE(SUM(employer_tax_total),0)                   AS employer_tax_cost,
                   COALESCE(SUM(employer_benefit_total),0)               AS employer_benefit_cost,
                   COALESCE(SUM(net_pay_amount),0)                       AS net_pay
            FROM   base
            GROUP  BY grp_id, grp_name
            ORDER  BY grp_name
            """;
        return MapOrgRows(await conn.QueryAsync<dynamic>(recSql, new { RunId = runId, TargetTypeCode = dim }));
    }

    // Returns the employment IDs of all employees in this run whose org chain passes through
    // the given org unit (at any level — leaf, intermediate, or root of their chain).
    public async Task<IReadOnlyList<Guid>> GetEmploymentIdsForOrgUnitAsync(Guid runId, Guid orgUnitId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var ids = await conn.QueryAsync<Guid>(
            """
            WITH RECURSIVE
            org_walk(employment_id, org_unit_id, parent_org_unit_id, depth) AS (
                SELECT r.employment_id, ou.org_unit_id, ou.parent_org_unit_id, 0
                FROM   employee_payroll_result r
                JOIN   employment e ON e.employment_id = r.employment_id
                LEFT   JOIN assignment a
                       ON  a.employment_id        = e.employment_id
                       AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                       AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')
                LEFT   JOIN org_unit ou ON ou.org_unit_id = a.department_id
                WHERE  r.payroll_run_id = @RunId
                UNION ALL
                SELECT ow.employment_id, p.org_unit_id, p.parent_org_unit_id, ow.depth + 1
                FROM   org_walk ow
                JOIN   org_unit p ON p.org_unit_id = ow.parent_org_unit_id
                WHERE  ow.parent_org_unit_id IS NOT NULL
                  AND  ow.depth < 10
            )
            SELECT DISTINCT employment_id
            FROM   org_walk
            WHERE  org_unit_id = @OrgUnitId
            """,
            new { RunId = runId, OrgUnitId = orgUnitId });
        return ids.ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetEmploymentIdsForJobAsync(Guid runId, Guid jobId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var ids = await conn.QueryAsync<Guid>(
            """
            SELECT DISTINCT r.employment_id
            FROM   employee_payroll_result r
            JOIN   employment e ON e.employment_id = r.employment_id
            WHERE  r.payroll_run_id = @RunId
              AND  e.job_id = @JobId
            """,
            new { RunId = runId, JobId = jobId });
        return ids.ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetEmploymentIdsForLocationAsync(Guid runId, Guid locationId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var ids = await conn.QueryAsync<Guid>(
            """
            SELECT DISTINCT r.employment_id
            FROM   employee_payroll_result r
            JOIN   employment e ON e.employment_id = r.employment_id
            JOIN   assignment a ON a.employment_id        = e.employment_id
                               AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                               AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')
            WHERE  r.payroll_run_id = @RunId
              AND  a.location_id = @LocationId
            """,
            new { RunId = runId, LocationId = locationId });
        return ids.ToList();
    }

    private static IReadOnlyList<PayRegisterOrgRow> MapOrgRows(IEnumerable<dynamic> rows) =>
        rows.Select(r =>
        {
            decimal gross = (decimal)r.gross_wages;
            decimal erTax = (decimal)r.employer_tax_cost;
            decimal erBen = (decimal)r.employer_benefit_cost;
            object  rawId = r.grp_id;
            Guid groupId  = rawId is Guid g ? g : Guid.Empty;
            return new PayRegisterOrgRow(
                GroupId:             groupId,
                GroupName:           (string)r.grp_name,
                Headcount:           (int)(long)r.headcount,
                GrossWages:          gross,
                EmployeeTaxWithheld: (decimal)r.employee_tax_withheld,
                EmployerTaxCost:     erTax,
                EmployerBenefitCost: erBen,
                NetPay:              (decimal)r.net_pay,
                TotalEmployerCost:   gross + erTax + erBen);
        }).ToList();

    // ── Employee Detail ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PayRegisterEmployeeRow>> GetEmployeeDetailAsync(Guid runId)
    {
        using var conn = _connectionFactory.CreateConnection();

        // Base rows: result + person + employment + department + effective division
        var baseRows = (await conn.QueryAsync<dynamic>(
            """
            SELECT r.employee_payroll_result_id,
                   r.employment_id,
                   r.gross_pay_amount,
                   r.net_pay_amount,
                   e.employee_number,
                   p.legal_first_name || ' ' || p.legal_last_name AS employee_name,
                   COALESCE(d.org_unit_name, 'Unassigned') AS department_name,
                   CASE
                       WHEN dt.code  = 'DIVISION' THEN d.org_unit_id
                       WHEN dpt.code = 'DIVISION' THEN dp.org_unit_id
                       ELSE NULL
                   END AS division_id
            FROM   employee_payroll_result r
            JOIN   employment e ON e.employment_id = r.employment_id
            JOIN   person     p ON p.person_id     = e.person_id
            LEFT   JOIN assignment a
                   ON  a.employment_id        = e.employment_id
                   AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                   AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')
            LEFT   JOIN org_unit d    ON d.org_unit_id    = a.department_id
            LEFT   JOIN lkp_org_unit_type dt  ON dt.id    = d.org_unit_type_id
            LEFT   JOIN org_unit dp   ON dp.org_unit_id   = d.parent_org_unit_id
            LEFT   JOIN lkp_org_unit_type dpt ON dpt.id   = dp.org_unit_type_id
            WHERE  r.payroll_run_id = @RunId
            ORDER  BY e.employee_number
            """, new { RunId = runId })).ToList();

        if (baseRows.Count == 0)
            return [];

        // Tax breakdown per result
        var taxRows = (await conn.QueryAsync<dynamic>(
            """
            SELECT tl.employee_payroll_result_id, tl.tax_code, tl.employer_flag,
                   COALESCE(cs.calculation_category, '') AS calculation_category,
                   COALESCE(SUM(tl.calculated_amount), 0) AS amount
            FROM   tax_result_line tl
            JOIN   employee_payroll_result r ON r.employee_payroll_result_id = tl.employee_payroll_result_id
            LEFT   JOIN payroll_calculation_steps cs ON cs.step_code = tl.tax_code AND cs.is_active = TRUE
            WHERE  r.payroll_run_id = @RunId
            GROUP  BY tl.employee_payroll_result_id, tl.tax_code, tl.employer_flag, cs.calculation_category
            """, new { RunId = runId })).ToList();

        // Deduction breakdown per result
        var deductRows = (await conn.QueryAsync<dynamic>(
            """
            SELECT dl.employee_payroll_result_id, dl.deduction_code,
                   COALESCE(SUM(dl.calculated_amount), 0) AS amount
            FROM   deduction_result_line dl
            JOIN   employee_payroll_result r ON r.employee_payroll_result_id = dl.employee_payroll_result_id
            WHERE  r.payroll_run_id = @RunId
            GROUP  BY dl.employee_payroll_result_id, dl.deduction_code
            """, new { RunId = runId })).ToList();

        // Employer contribution totals per result
        var erContribRows = (await conn.QueryAsync<dynamic>(
            """
            SELECT ecl.employee_payroll_result_id,
                   COALESCE(SUM(ecl.calculated_amount), 0) AS amount
            FROM   employer_contribution_result_line ecl
            JOIN   employee_payroll_result r ON r.employee_payroll_result_id = ecl.employee_payroll_result_id
            WHERE  r.payroll_run_id = @RunId
            GROUP  BY ecl.employee_payroll_result_id
            """, new { RunId = runId })).ToList();

        // Index lookups
        var taxIndex    = taxRows.GroupBy(r => (Guid)r.employee_payroll_result_id)
                                 .ToDictionary(g => g.Key, g => g.ToList());
        var deductIndex = deductRows.GroupBy(r => (Guid)r.employee_payroll_result_id)
                                    .ToDictionary(g => g.Key, g => g.ToList());
        var erIndex     = erContribRows.ToDictionary(
                                 r => (Guid)r.employee_payroll_result_id,
                                 r => (decimal)r.amount);

        return baseRows.Select(row =>
        {
            var resultId = (Guid)row.employee_payroll_result_id;

            // Tax pivot
            decimal fedTax = 0, ssEe = 0, medEe = 0, stateTax = 0, localTax = 0, ssEr = 0, medEr = 0;
            if (taxIndex.TryGetValue(resultId, out var taxes))
            {
                foreach (var t in taxes)
                {
                    string  code     = ((string)t.tax_code).ToUpperInvariant();
                    bool    er       = (bool)t.employer_flag;
                    decimal amt      = (decimal)t.amount;
                    string  category = ((string)t.calculation_category).ToUpperInvariant();

                    switch (category)
                    {
                        case "SOCIAL_INSURANCE":
                            if (code.Contains("MEDICARE"))
                            { if (er) medEr += amt; else medEe += amt; }
                            else
                            { if (er) ssEr += amt; else ssEe += amt; }
                            break;
                        case "INCOME_TAX":
                            if (!er) fedTax += amt;
                            break;
                        case "SUBNATIONAL_TAX":
                        case "DERIVED_TAX":
                            if (!er) stateTax += amt;
                            break;
                    }
                }
            }

            // Deduction pivot
            decimal benefitDeducts = 0, retirement = 0, garnishments = 0;
            if (deductIndex.TryGetValue(resultId, out var deducts))
            {
                foreach (var d in deducts)
                {
                    string  code = ((string)d.deduction_code).ToUpperInvariant();
                    decimal amt  = (decimal)d.amount;
                    if      (code.Contains("RETIRE") || code.Contains("401") || code.Contains("403")) retirement   += amt;
                    else if (code.Contains("GARNISH"))                                                  garnishments += amt;
                    else                                                                                 benefitDeducts += amt;
                }
            }

            decimal erBenefit = erIndex.GetValueOrDefault(resultId);
            object  rawDivId  = row.division_id;
            Guid?   divId     = rawDivId is DBNull || rawDivId is null ? null : (Guid?)rawDivId;

            return new PayRegisterEmployeeRow(
                EmployeePayrollResultId: resultId,
                EmploymentId:            (Guid)row.employment_id,
                EmployeeNumber:          (string)row.employee_number,
                EmployeeName:            (string)row.employee_name,
                DepartmentName:          (string)row.department_name,
                DivisionId:              divId,
                Gross:                   (decimal)row.gross_pay_amount,
                FederalTax:              fedTax,
                SsEe:                    ssEe,
                MedicareEe:              medEe,
                StateTax:                stateTax,
                LocalTax:                localTax,
                BenefitDeductions:       benefitDeducts,
                RetirementDeductions:    retirement,
                Garnishments:            garnishments,
                NetPay:                  (decimal)row.net_pay_amount,
                EmployerSs:              ssEr,
                EmployerMedicare:        medEr,
                EmployerBenefit:         erBenefit);
        }).ToList();
    }

    // ── Line Detail (expansion) ───────────────────────────────────────────────

    public async Task<IReadOnlyList<PayRegisterLineItem>> GetLineDetailAsync(Guid employeePayrollResultId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = new List<PayRegisterLineItem>();

        var earnings = await conn.QueryAsync<dynamic>(
            """
            SELECT earnings_code AS code, earnings_description AS description, calculated_amount AS amount
            FROM   earnings_result_line
            WHERE  employee_payroll_result_id = @Id
            ORDER  BY earnings_code
            """, new { Id = employeePayrollResultId });
        result.AddRange(earnings.Select(r =>
            new PayRegisterLineItem("Earnings", (string)r.code, (string)r.description, (decimal)r.amount)));

        var deductions = await conn.QueryAsync<dynamic>(
            """
            SELECT deduction_code AS code, deduction_description AS description, calculated_amount AS amount
            FROM   deduction_result_line
            WHERE  employee_payroll_result_id = @Id
            ORDER  BY deduction_code
            """, new { Id = employeePayrollResultId });
        result.AddRange(deductions.Select(r =>
            new PayRegisterLineItem("Deduction", (string)r.code, (string)r.description, (decimal)r.amount)));

        var taxes = await conn.QueryAsync<dynamic>(
            """
            SELECT tax_code AS code, tax_description AS description, calculated_amount AS amount,
                   employer_flag
            FROM   tax_result_line
            WHERE  employee_payroll_result_id = @Id
            ORDER  BY employer_flag, tax_code
            """, new { Id = employeePayrollResultId });
        result.AddRange(taxes.Select(r =>
            new PayRegisterLineItem(
                (bool)r.employer_flag ? "Employer Tax" : "Employee Tax",
                (string)r.code, (string)r.description, (decimal)r.amount)));

        var erContrib = await conn.QueryAsync<dynamic>(
            """
            SELECT contribution_code AS code, contribution_description AS description, calculated_amount AS amount
            FROM   employer_contribution_result_line
            WHERE  employee_payroll_result_id = @Id
            ORDER  BY contribution_code
            """, new { Id = employeePayrollResultId });
        result.AddRange(erContrib.Select(r =>
            new PayRegisterLineItem("Employer Contribution", (string)r.code, (string)r.description, (decimal)r.amount)));

        return result;
    }
}
