using AllWorkHRIS.Module.Benefits.Domain.Codes;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public interface IDeferralLimitGroupRepository
{
    /// <summary>
    /// Returns the §402(g) deferral-limit groups active on <paramref name="asOf"/>, each with its
    /// active member set. Trust-based scoping: when <paramref name="legalEntityId"/> is supplied the
    /// entity-specific row wins over the global default (legal_entity_id IS NULL) for a given group
    /// code; when null, only the global defaults are returned.
    /// </summary>
    Task<IReadOnlyList<DeferralLimitGroup>> GetActiveGroupsAsync(
        DateOnly asOf, Guid? legalEntityId = null, CancellationToken ct = default);
}
