using Dapper;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class PayrollContextLookup : IPayrollContextLookup
{
    private readonly IPayrollContextRepository _repo;
    private readonly IConnectionFactory        _connectionFactory;

    public PayrollContextLookup(IPayrollContextRepository repo, IConnectionFactory connectionFactory)
    {
        _repo              = repo;
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsAsync()
    {
        var contexts = await _repo.GetAllActiveAsync();
        return contexts.Select(c => (c.PayrollContextId, c.PayrollContextName)).ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsByLegalEntityAsync(Guid legalEntityId)
    {
        var contexts = await _repo.GetByLegalEntityAsync(legalEntityId);
        return contexts.Where(c => c.ContextStatus == "ACTIVE")
                       .Select(c => (c.PayrollContextId, c.PayrollContextName))
                       .ToList();
    }

    public async Task<decimal> GetOtThresholdForEmploymentAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var threshold = await conn.ExecuteScalarAsync<decimal?>(
            """
            SELECT pc.ot_weekly_threshold_hours
            FROM   payroll_profile pp
            JOIN   payroll_context pc ON pc.payroll_context_id = pp.payroll_context_id
            WHERE  pp.employment_id = @EmploymentId
            """,
            new { EmploymentId = employmentId });
        return threshold ?? 40m;
    }
}
