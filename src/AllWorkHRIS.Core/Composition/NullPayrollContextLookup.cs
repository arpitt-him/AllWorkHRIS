namespace AllWorkHRIS.Core.Composition;

public sealed class NullPayrollContextLookup : IPayrollContextLookup
{
    public Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsAsync()
        => Task.FromResult<IReadOnlyList<(Guid Id, string Name)>>([]);
}
