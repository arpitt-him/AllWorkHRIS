using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Benefits.Domain.Codes;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public sealed class DeductionEmployerMatchRepository : IDeductionEmployerMatchRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public DeductionEmployerMatchRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    private const string SelectColumns = """
        match_id, deduction_id, employee_group_id, match_rate,
        match_cap_pct_of_gross, match_cap_annual_amount, match_type,
        effective_from, effective_to, created_at
        """;

    public async Task<DeductionEmployerMatch?> GetActiveByDeductionIdAsync(
        Guid deductionId, DateOnly asOf, Guid? employeeGroupId = null, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();

        if (employeeGroupId.HasValue)
        {
            // Try group-specific rule first
            var sql = $"""
                SELECT {SelectColumns}
                FROM   deduction_employer_match
                WHERE  deduction_id      = @DeductionId
                  AND  employee_group_id = @EmployeeGroupId
                  AND  effective_from   <= @AsOf
                  AND  (effective_to IS NULL OR effective_to >= @AsOf)
                ORDER  BY effective_from DESC
                LIMIT 1
                """;
            var specific = await conn.QuerySingleOrDefaultAsync<DeductionEmployerMatch>(
                sql, new { DeductionId = deductionId, EmployeeGroupId = employeeGroupId.Value, AsOf = asOf });
            if (specific is not null) return specific;
        }

        // Fall back to universal rule (employee_group_id IS NULL)
        var universalSql = $"""
            SELECT {SelectColumns}
            FROM   deduction_employer_match
            WHERE  deduction_id      = @DeductionId
              AND  employee_group_id IS NULL
              AND  effective_from   <= @AsOf
              AND  (effective_to IS NULL OR effective_to >= @AsOf)
            ORDER  BY effective_from DESC
            LIMIT 1
            """;
        return await conn.QuerySingleOrDefaultAsync<DeductionEmployerMatch>(
            universalSql, new { DeductionId = deductionId, AsOf = asOf });
    }

    public async Task<IEnumerable<DeductionEmployerMatch>> GetAllByDeductionIdAsync(
        Guid deductionId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            SELECT {SelectColumns}
            FROM   deduction_employer_match
            WHERE  deduction_id = @DeductionId
            ORDER  BY effective_from DESC
            """;
        return await conn.QueryAsync<DeductionEmployerMatch>(sql, new { DeductionId = deductionId });
    }

    public async Task<Guid> InsertAsync(DeductionEmployerMatch match, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO deduction_employer_match
                (match_id, deduction_id, employee_group_id, match_rate,
                 match_cap_pct_of_gross, match_cap_annual_amount, match_type,
                 effective_from, effective_to, created_at)
            VALUES
                (@MatchId, @DeductionId, @EmployeeGroupId, @MatchRate,
                 @MatchCapPctOfGross, @MatchCapAnnualAmount, @MatchType,
                 @EffectiveFrom, @EffectiveTo, @CreatedAt)
            """;
        await uow.Connection.ExecuteAsync(sql, match, uow.Transaction);
        return match.MatchId;
    }

    public async Task UpdateAsync(DeductionEmployerMatch match, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE deduction_employer_match
            SET    match_rate              = @MatchRate,
                   match_cap_pct_of_gross  = @MatchCapPctOfGross,
                   match_cap_annual_amount = @MatchCapAnnualAmount,
                   match_type              = @MatchType,
                   effective_to            = @EffectiveTo
            WHERE  match_id = @MatchId
            """;
        await uow.Connection.ExecuteAsync(sql, match, uow.Transaction);
    }

    public async Task<Guid> InsertAsync(DeductionEmployerMatch match)
    {
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var id = await InsertAsync(match, uow);
            uow.Commit();
            return id;
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task UpdateAsync(DeductionEmployerMatch match)
    {
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await UpdateAsync(match, uow);
            uow.Commit();
        }
        catch { uow.Rollback(); throw; }
    }
}
