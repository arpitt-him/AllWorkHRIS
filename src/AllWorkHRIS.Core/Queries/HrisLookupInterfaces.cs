namespace AllWorkHRIS.Core.Queries;

public sealed record OrgUnitOption(Guid OrgUnitId, string OrgUnitName);

public interface IEmploymentLookup
{
    Task<IReadOnlyList<EmploymentListItem>> GetAllActiveListAsync(Guid? legalEntityId = null);
}

public interface IPersonNameLookup
{
    Task<Dictionary<Guid, string>> GetNamesByEmploymentIdsAsync(IEnumerable<Guid> employmentIds);
}

public interface IOrgUnitLookup
{
    Task<IReadOnlyList<OrgUnitOption>> GetDivisionsAsync(Guid? legalEntityId = null);
    Task<IReadOnlyList<OrgUnitOption>> GetDepartmentsAsync(Guid? legalEntityId = null);
    Task<IReadOnlyList<OrgUnitOption>> GetLocationsAsync(Guid? legalEntityId = null);
}
