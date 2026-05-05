namespace AllWorkHRIS.Module.Benefits.Commands;

public sealed record CorrectElectionCommand
{
    public required Guid     ElectionId                 { get; init; }

    // Null = carry forward from prior election.
    public DateOnly?         EffectiveStartDate         { get; init; }
    public DateOnly?         EffectiveEndDate           { get; init; }
    public decimal?          EmployeeAmount             { get; init; }
    public decimal?          EmployerContributionAmount { get; init; }
    public decimal?          ContributionPct            { get; init; }
    public string?           CoverageTier               { get; init; }
    public decimal?          AnnualCoverageAmount       { get; init; }

    public required string   CorrectionType             { get; init; }  // AMOUNT_CHANGE | DATE_CHANGE | DATA_ENTRY_ERROR
    public required Guid     CorrectedBy                { get; init; }
}
