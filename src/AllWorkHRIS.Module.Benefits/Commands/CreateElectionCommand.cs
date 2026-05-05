namespace AllWorkHRIS.Module.Benefits.Commands;

public sealed record CreateElectionCommand
{
    public required Guid     EmploymentId               { get; init; }
    public required Guid     DeductionId                { get; init; }
    public decimal?          EmployeeAmount             { get; init; }
    public decimal?          EmployerContributionAmount { get; init; }
    public decimal?          ContributionPct            { get; init; }
    public string?           CoverageTier               { get; init; }
    public decimal?          AnnualCoverageAmount       { get; init; }
    public required DateOnly EffectiveStartDate         { get; init; }
    public DateOnly?         EffectiveEndDate           { get; init; }
    public required string   Source                     { get; init; }  // MANUAL | IMPORT | API
    public required Guid     CreatedBy                  { get; init; }
}
