namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record OrgUnit
{
    public Guid OrgUnitId { get; init; }
    public OrgUnitType OrgUnitType { get; init; }
    public string OrgUnitCode { get; init; } = default!;
    public string OrgUnitName { get; init; } = default!;
    public Guid? ParentOrgUnitId { get; init; }
    public OrgStatus OrgStatus { get; init; }
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
    public WorkLocationType? WorkLocationType { get; init; }
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
    public FlsaClassification FlsaClassification { get; init; }
    public EeoCategory EeoCategory { get; init; }
    public JobStatus JobStatus { get; init; }
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
    public PositionStatus PositionStatus { get; init; }
    public DateOnly EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public Guid LastUpdatedBy { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
}
