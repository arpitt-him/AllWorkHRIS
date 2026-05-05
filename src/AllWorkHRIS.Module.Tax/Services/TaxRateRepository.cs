using System.Data;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Tax.Queries;
using AllWorkHRIS.Module.Tax.Repositories;
using Dapper;

namespace AllWorkHRIS.Module.Tax.Services;

public sealed class TaxRateRepository : ITaxRateRepository
{
    private readonly IConnectionFactory _db;

    public TaxRateRepository(IConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<CalculationStepRow>> GetActiveStepsAsync(
        string jurisdictionCode, DateOnly payDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT s.step_code              AS StepCode,
                   s.step_type             AS StepType,
                   s.calculation_category  AS CalculationCategory,
                   s.sequence_number       AS SequenceNumber,
                   s.applies_to            AS AppliesTo,
                   s.reduces_income_tax_wages AS ReducesIncomeTaxWages,
                   s.reduces_fica_wages    AS ReducesFicaWages
            FROM   payroll_calculation_steps s
            JOIN   tax_jurisdiction j ON j.jurisdiction_id = s.jurisdiction_id
            WHERE  j.jurisdiction_code = @JurisdictionCode
              AND  s.status_code = 'ACTIVE'
              AND  s.is_active   = TRUE
            ORDER  BY s.sequence_number
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<CalculationStepRow>(sql,
            new { JurisdictionCode = jurisdictionCode });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<BracketRow>> GetBracketsAsync(
        string stepCode, string? filingStatusCode, DateOnly payDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT lower_limit  AS LowerLimit,
                   upper_limit  AS UpperLimit,
                   rate         AS Rate
            FROM   tax_brackets
            WHERE  step_code          = @StepCode
              AND  (filing_status_code = @FilingStatusCode OR filing_status_code IS NULL)
              AND  effective_from <= @PayDate
              AND  (effective_to  IS NULL OR effective_to >= @PayDate)
            ORDER  BY lower_limit
            """;

        var p = new DynamicParameters();
        p.Add("StepCode",          stepCode,          DbType.String);
        p.Add("FilingStatusCode",  filingStatusCode,  DbType.String);
        p.Add("PayDate",           payDate);

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<BracketRow>(sql, p);
        return rows.AsList();
    }

    public async Task<FlatRateRow?> GetFlatRateAsync(
        string stepCode, DateOnly payDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT rate                 AS Rate,
                   wage_base            AS WageBase,
                   period_cap_amount    AS PeriodCap,
                   annual_cap_amount    AS AnnualCap,
                   depends_on_step_code AS DependsOnStepCode
            FROM   tax_flat_rates
            WHERE  step_code      = @StepCode
              AND  effective_from <= @PayDate
              AND  (effective_to  IS NULL OR effective_to >= @PayDate)
            """;

        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<FlatRateRow>(sql,
            new { StepCode = stepCode, PayDate = payDate });
    }

    public async Task<IReadOnlyList<TieredBracketRow>> GetTieredBracketsAsync(
        string stepCode, DateOnly payDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT lower_limit       AS LowerLimit,
                   upper_limit       AS UpperLimit,
                   rate              AS Rate,
                   period_cap_amount AS PeriodCap,
                   annual_cap_amount AS AnnualCap
            FROM   tax_tiered_brackets
            WHERE  step_code      = @StepCode
              AND  effective_from <= @PayDate
              AND  (effective_to  IS NULL OR effective_to >= @PayDate)
            ORDER  BY lower_limit
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<TieredBracketRow>(sql,
            new { StepCode = stepCode, PayDate = payDate });
        return rows.AsList();
    }

    public async Task<AllowanceRow?> GetAllowanceAsync(
        string stepCode, string? filingStatusCode, DateOnly payDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT annual_amount AS AnnualAmount
            FROM   tax_allowances
            WHERE  step_code          = @StepCode
              AND  (filing_status_code = @FilingStatusCode OR filing_status_code IS NULL)
              AND  effective_from <= @PayDate
              AND  (effective_to  IS NULL OR effective_to >= @PayDate)
            LIMIT  1
            """;

        var p = new DynamicParameters();
        p.Add("StepCode",         stepCode,         DbType.String);
        p.Add("FilingStatusCode", filingStatusCode, DbType.String);
        p.Add("PayDate",          payDate);

        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<AllowanceRow>(sql, p);
    }

    public async Task<CreditRow?> GetCreditAsync(
        string stepCode, DateOnly payDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT annual_amount  AS AnnualAmount,
                   credit_rate    AS CreditRate,
                   is_refundable  AS IsRefundable
            FROM   tax_credits
            WHERE  step_code      = @StepCode
              AND  effective_from <= @PayDate
              AND  (effective_to  IS NULL OR effective_to >= @PayDate)
            """;

        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<CreditRow>(sql,
            new { StepCode = stepCode, PayDate = payDate });
    }
}
