namespace AllWorkHRIS.Module.Benefits.Commands;

public sealed record AmendElectionCommand
{
    public required Guid     ElectionId                 { get; init; }

    // The new election will start from AmendmentDate; the prior election is trimmed to AmendmentDate - 1.
    public required DateOnly AmendmentDate              { get; init; }

    // Null = carry forward from prior election.
    public decimal?          EmployeeAmount             { get; init; }
    public decimal?          EmployerContributionAmount { get; init; }
    public decimal?          ContributionPct            { get; init; }
    public decimal?          EmployerContributionPct    { get; init; }
    public string?           CoverageTier               { get; init; }
    public decimal?          AnnualCoverageAmount       { get; init; }

    public required Guid     AmendedBy                  { get; init; }
}
