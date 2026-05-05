namespace AllWorkHRIS.Core.Pipeline;

public sealed class NullEmploymentJurisdictionLookup : IEmploymentJurisdictionLookup
{
    public Task<IReadOnlyList<JurisdictionRef>> GetJurisdictionsAsync(
        Guid employmentId, DateOnly payDate, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JurisdictionRef>>([]);
}
