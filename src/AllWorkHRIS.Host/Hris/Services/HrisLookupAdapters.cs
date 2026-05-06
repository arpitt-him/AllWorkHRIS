using AllWorkHRIS.Core.Queries;

namespace AllWorkHRIS.Host.Hris.Services;

public sealed class EmploymentLookupAdapter : IEmploymentLookup
{
    private readonly IEmploymentService _svc;
    public EmploymentLookupAdapter(IEmploymentService svc) => _svc = svc;

    public async Task<IReadOnlyList<EmploymentListItem>> GetAllActiveListAsync(Guid? legalEntityId = null)
    {
        var items = await _svc.GetAllActiveListAsync(legalEntityId);
        return items.Select(x => new EmploymentListItem
        {
            EmploymentId        = x.EmploymentId,
            PersonId            = x.PersonId,
            LegalFirstName      = x.LegalFirstName,
            LegalLastName       = x.LegalLastName,
            PreferredName       = x.PreferredName,
            EmployeeNumber      = x.EmployeeNumber,
            EmploymentStatus    = x.EmploymentStatus,
            EmploymentType      = x.EmploymentType,
            EmploymentStartDate = x.EmploymentStartDate,
            JobTitle            = x.JobTitle,
            DivisionName        = x.DivisionName,
            DivisionId          = x.DivisionId,
            DepartmentName      = x.DepartmentName,
            DepartmentId        = x.DepartmentId,
            LocationName        = x.LocationName,
            LocationId          = x.LocationId,
            RateTypeId          = x.RateTypeId,
            RateTypeCode        = x.RateTypeCode,
            PayFrequencyId      = x.PayFrequencyId,
            PayFrequencyCode    = x.PayFrequencyCode
        }).ToList();
    }
}

public sealed class PersonNameLookupAdapter : IPersonNameLookup
{
    private readonly IPersonService _svc;
    public PersonNameLookupAdapter(IPersonService svc) => _svc = svc;

    public Task<Dictionary<Guid, string>> GetNamesByEmploymentIdsAsync(IEnumerable<Guid> employmentIds)
        => _svc.GetNamesByEmploymentIdsAsync(employmentIds);
}

public sealed class OrgUnitLookupAdapter : IOrgUnitLookup
{
    private readonly IOrgStructureService _svc;
    public OrgUnitLookupAdapter(IOrgStructureService svc) => _svc = svc;

    public async Task<IReadOnlyList<OrgUnitOption>> GetDivisionsAsync(Guid? legalEntityId = null)
    {
        var items = await _svc.GetDivisionsAsync(legalEntityId);
        return items.Select(x => new OrgUnitOption(x.OrgUnitId, x.OrgUnitName)).ToList();
    }

    public async Task<IReadOnlyList<OrgUnitOption>> GetDepartmentsAsync(Guid? legalEntityId = null)
    {
        var items = await _svc.GetDepartmentsAsync(legalEntityId);
        return items.Select(x => new OrgUnitOption(x.OrgUnitId, x.OrgUnitName)).ToList();
    }

    public async Task<IReadOnlyList<OrgUnitOption>> GetLocationsAsync(Guid? legalEntityId = null)
    {
        var items = await _svc.GetLocationsAsync(legalEntityId);
        return items.Select(x => new OrgUnitOption(x.OrgUnitId, x.OrgUnitName)).ToList();
    }
}
