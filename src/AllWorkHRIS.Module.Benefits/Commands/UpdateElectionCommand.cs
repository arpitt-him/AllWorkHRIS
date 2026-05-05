namespace AllWorkHRIS.Module.Benefits.Commands;

public sealed record UpdateElectionCommand
{
    public required Guid     ElectionId                 { get; init; }
    public required decimal  EmployeeAmount             { get; init; }
    public decimal?          EmployerContributionAmount { get; init; }
    public required DateOnly EffectiveStartDate         { get; init; }
    public DateOnly?         EffectiveEndDate           { get; init; }
    public required string   CorrectionType             { get; init; }  // AMOUNT_CHANGE | DATE_CHANGE
    public required Guid     UpdatedBy                  { get; init; }
}
