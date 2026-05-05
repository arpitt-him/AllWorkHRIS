namespace AllWorkHRIS.Core.Pipeline;

public sealed record JurisdictionRef(int JurisdictionId, string JurisdictionCode);

public interface IEmploymentJurisdictionLookup
{
    Task<IReadOnlyList<JurisdictionRef>> GetJurisdictionsAsync(
        Guid employmentId, DateOnly payDate, CancellationToken ct = default);
}
