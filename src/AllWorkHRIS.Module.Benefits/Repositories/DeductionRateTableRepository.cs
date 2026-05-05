using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Benefits.Domain.Codes;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public sealed class DeductionRateTableRepository : IDeductionRateTableRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public DeductionRateTableRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    private const string TableColumns = """
        rate_table_id, deduction_id, rate_type, effective_from, effective_to, description, created_at
        """;

    private const string EntryColumns = """
        rate_entry_id, rate_table_id, tier_code, band_min, band_max, employee_rate, employer_rate
        """;

    public async Task<DeductionRateTable?> GetActiveByDeductionIdAsync(
        Guid deductionId, DateOnly asOf, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            SELECT {TableColumns}
            FROM   deduction_rate_table
            WHERE  deduction_id   = @DeductionId
              AND  effective_from <= @AsOf
              AND  (effective_to IS NULL OR effective_to >= @AsOf)
            ORDER  BY effective_from DESC
            LIMIT 1
            """;
        return await conn.QuerySingleOrDefaultAsync<DeductionRateTable>(sql, new { DeductionId = deductionId, AsOf = asOf });
    }

    public async Task<IEnumerable<DeductionRateTable>> GetAllByDeductionIdAsync(
        Guid deductionId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            SELECT {TableColumns}
            FROM   deduction_rate_table
            WHERE  deduction_id = @DeductionId
            ORDER  BY effective_from DESC
            """;
        return await conn.QueryAsync<DeductionRateTable>(sql, new { DeductionId = deductionId });
    }

    public async Task<IEnumerable<DeductionRateEntry>> GetEntriesAsync(
        Guid rateTableId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            SELECT {EntryColumns}
            FROM   deduction_rate_entry
            WHERE  rate_table_id = @RateTableId
            ORDER  BY tier_code NULLS LAST, band_min NULLS LAST
            """;
        return await conn.QueryAsync<DeductionRateEntry>(sql, new { RateTableId = rateTableId });
    }

    public async Task<Guid> InsertTableAsync(DeductionRateTable table, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO deduction_rate_table
                (rate_table_id, deduction_id, rate_type, effective_from, effective_to, description, created_at)
            VALUES
                (@RateTableId, @DeductionId, @RateType, @EffectiveFrom, @EffectiveTo, @Description, @CreatedAt)
            """;
        await uow.Connection.ExecuteAsync(sql, table, uow.Transaction);
        return table.RateTableId;
    }

    public async Task<Guid> InsertEntryAsync(DeductionRateEntry entry, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO deduction_rate_entry
                (rate_entry_id, rate_table_id, tier_code, band_min, band_max, employee_rate, employer_rate)
            VALUES
                (@RateEntryId, @RateTableId, @TierCode, @BandMin, @BandMax, @EmployeeRate, @EmployerRate)
            """;
        await uow.Connection.ExecuteAsync(sql, entry, uow.Transaction);
        return entry.RateEntryId;
    }

    public async Task ReplaceEntriesAsync(
        Guid rateTableId, IEnumerable<DeductionRateEntry> entries, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            "DELETE FROM deduction_rate_entry WHERE rate_table_id = @RateTableId",
            new { RateTableId = rateTableId }, uow.Transaction);

        foreach (var entry in entries)
            await InsertEntryAsync(entry, uow);
    }

    public async Task<Guid> InsertTableAsync(DeductionRateTable table)
    {
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var id = await InsertTableAsync(table, uow);
            uow.Commit();
            return id;
        }
        catch { uow.Rollback(); throw; }
    }
}
