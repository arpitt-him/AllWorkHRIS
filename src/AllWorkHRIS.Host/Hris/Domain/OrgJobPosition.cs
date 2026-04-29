namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record OrgUnit
{
    public Guid OrgUnitId { get; init; }
    public int OrgUnitTypeId { get; init; }
    public string OrgUnitCode { get; init; } = default!;
    public string OrgUnitName { get; init; } = default!;
    public Guid? ParentOrgUnitId { get; init; }
    public int OrgStatusId { get; init; }
    public DateOnly EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public string? TaxRegistrationNumber { get; init; }
    public string? CountryCode { get; init; }
    public string? StateOfIncorporation { get; init; }
    public string? LegalEntityType { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? StateCode { get; init; }
    public string? PostalCode { get; init; }
    public string? LocalityCode { get; init; }
    public int? WorkLocationTypeId { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public Guid LastUpdatedBy { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
}

public sealed record Job
{
    public Guid JobId { get; init; }
    public string JobCode { get; init; } = default!;
    public string JobTitle { get; init; } = default!;
    public string? JobFamily { get; init; }
    public string? JobLevel { get; init; }
    public int FlsaClassificationId { get; init; }
    public int EeoCategoryId { get; init; }
    public int JobStatusId { get; init; }
    public DateOnly EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public Guid LastUpdatedBy { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
}

public sealed record Position
{
    public Guid PositionId { get; init; }
    public Guid JobId { get; init; }
    public Guid OrgUnitId { get; init; }
    public string? PositionTitle { get; init; }
    public int? HeadcountBudget { get; init; }
    public int PositionStatusId { get; init; }
    public DateOnly EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public Guid LastUpdatedBy { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
}
