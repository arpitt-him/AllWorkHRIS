using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Payroll.Domain.Earnings;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public sealed class EarningsCodeRepository : IEarningsCodeRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public EarningsCodeRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<EarningsCode>> GetAllActiveAsync(DateOnly asOf)
    {
        const string sql = """
            SELECT * FROM earnings_code
            WHERE status = 'ACTIVE'
              AND effective_start_date <= @AsOf
              AND (effective_end_date IS NULL OR effective_end_date >= @AsOf)
            ORDER BY code
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<EarningsCode>(sql,
            new { AsOf = asOf.ToDateTime(TimeOnly.MinValue) })).ToList();
    }
}
