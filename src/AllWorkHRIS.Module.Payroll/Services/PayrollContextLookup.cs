using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class PayrollContextLookup : IPayrollContextLookup
{
    private readonly IPayrollContextRepository _repo;

    public PayrollContextLookup(IPayrollContextRepository repo) => _repo = repo;

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
}
