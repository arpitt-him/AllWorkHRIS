using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Benefits.Domain.Codes;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public sealed class DeductionRepository : IDeductionRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public DeductionRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    private const string SelectColumns = """
        deduction_id, code, description, tax_treatment, status,
        calculation_mode, wage_base, age_as_of_rule,
        effective_start_date, effective_end_date, created_at, updated_at
        """;

    public async Task<Deduction?> GetByIdAsync(Guid deductionId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"SELECT {SelectColumns} FROM deduction WHERE deduction_id = @DeductionId";
        return await conn.QuerySingleOrDefaultAsync<Deduction>(sql, new { DeductionId = deductionId });
    }

    public async Task<Deduction?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"SELECT {SelectColumns} FROM deduction WHERE code = @Code";
        return await conn.QuerySingleOrDefaultAsync<Deduction>(sql, new { Code = code });
    }

    public async Task<IEnumerable<Deduction>> GetActiveCodesAsync(CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            SELECT {SelectColumns}
            FROM   deduction
            WHERE  status = 'ACTIVE'
            ORDER  BY code
            """;
        return await conn.QueryAsync<Deduction>(sql);
    }

    public async Task<IEnumerable<Deduction>> GetAllCodesAsync(CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            SELECT {SelectColumns}
            FROM   deduction
            ORDER  BY code
            """;
        return await conn.QueryAsync<Deduction>(sql);
    }

    public async Task<Guid> InsertAsync(Deduction deduction, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO deduction
                (deduction_id, code, description, tax_treatment, status,
                 calculation_mode, wage_base, age_as_of_rule,
                 effective_start_date, effective_end_date, created_at, updated_at)
            VALUES
                (@DeductionId, @Code, @Description, @TaxTreatment, @Status,
                 @CalculationMode, @WageBase, @AgeAsOfRule,
                 @EffectiveStartDate, @EffectiveEndDate, @CreatedAt, @UpdatedAt)
            """;
        await uow.Connection.ExecuteAsync(sql, deduction, uow.Transaction);
        return deduction.DeductionId;
    }

    public async Task UpdateAsync(Deduction deduction, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE deduction
            SET    description        = @Description,
                   status             = @Status,
                   calculation_mode   = @CalculationMode,
                   wage_base          = @WageBase,
                   age_as_of_rule     = @AgeAsOfRule,
                   effective_end_date = @EffectiveEndDate,
                   updated_at         = @UpdatedAt
            WHERE  deduction_id = @DeductionId
            """;
        await uow.Connection.ExecuteAsync(sql, deduction, uow.Transaction);
    }

    public async Task<Guid> InsertAsync(Deduction deduction)
    {
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var id = await InsertAsync(deduction, uow);
            uow.Commit();
            return id;
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task UpdateAsync(Deduction deduction)
    {
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await UpdateAsync(deduction, uow);
            uow.Commit();
        }
        catch { uow.Rollback(); throw; }
    }
}
