using AllWorkHRIS.Core.Domain;

namespace AllWorkHRIS.Module.Benefits.Queries;

public sealed record ElectionListItem
{
    public Guid     ElectionId                 { get; init; }
    public Guid     EmploymentId               { get; init; }
    public string   EmployeeNumber             { get; init; } = string.Empty;
    public string   EmployeeDisplayName        { get; init; } = string.Empty;
    public string   DeductionCode              { get; init; } = string.Empty;
    public string   DeductionDescription       { get; init; } = string.Empty;
    public string   CalculationMode            { get; init; } = string.Empty;
    public string   TaxTreatment               { get; init; } = string.Empty;
    public decimal  EmployeeAmount             { get; init; }
    public decimal? EmployerContributionAmount { get; init; }
    public decimal? ContributionPct            { get; init; }
    public string?  CoverageTier               { get; init; }
    public DateOnly EffectiveStartDate         { get; init; }
    public DateOnly? EffectiveEndDate          { get; init; }
    public string   Status                     { get; init; } = string.Empty;
    public string   Source                     { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt            { get; init; }
}

public sealed record ElectionListQuery
{
    public int       Page           { get; init; } = 1;
    public int       PageSize       { get; init; } = 20;
    public Guid?     LegalEntityId  { get; init; }
    public Guid?     EmploymentId   { get; init; }
    public string?   DeductionCode  { get; init; }
    public string?   Status         { get; init; }
    public DateOnly? EffectiveFrom  { get; init; }
    public DateOnly? EffectiveTo    { get; init; }
}

public sealed record BatchValidationResult
{
    public int                         TotalRecords   { get; init; }
    public int                         ValidCount     { get; init; }
    public int                         InvalidCount   { get; init; }
    public IReadOnlyList<RecordError>  Errors         { get; init; } = [];
}

public sealed record RecordError
{
    public int    RowNumber    { get; init; }
    public string Field        { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}
