using AllWorkHRIS.Core.Domain.Time;

namespace AllWorkHRIS.Core.Composition;

public sealed class NullPayrollContextLookup : IPayrollContextLookup
{
    public Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsAsync()
        => Task.FromResult<IReadOnlyList<(Guid Id, string Name)>>([]);

    public Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsByLegalEntityAsync(Guid legalEntityId)
        => Task.FromResult<IReadOnlyList<(Guid Id, string Name)>>([]);

    public Task<OtConfig> ResolveOtConfigAsync(Guid payrollContextId, DateOnly asOf)
        => Task.FromResult(new OtConfig(40m, 1, null));

    public Task<PeriodOtConfig> ResolveOtConfigForPeriodAsync(Guid payrollContextId, DateOnly periodStart, DateOnly periodEnd)
        => Task.FromResult(new PeriodOtConfig(1, new Dictionary<DateOnly, decimal>(), 40m, null));

    // Empty = "no override" → callers fall back to the is_worked_time flag (today's behavior).
    public Task<IReadOnlyCollection<int>> ResolveOtEligibleCategoriesAsync(Guid payrollContextId, DateOnly asOf)
        => Task.FromResult<IReadOnlyCollection<int>>([]);
}
