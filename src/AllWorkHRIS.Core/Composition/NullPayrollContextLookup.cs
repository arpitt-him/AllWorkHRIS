namespace AllWorkHRIS.Core.Composition;

public sealed class NullPayrollContextLookup : IPayrollContextLookup
{
    public Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsAsync()
        => Task.FromResult<IReadOnlyList<(Guid Id, string Name)>>([]);

    public Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsByLegalEntityAsync(Guid legalEntityId)
        => Task.FromResult<IReadOnlyList<(Guid Id, string Name)>>([]);
}
