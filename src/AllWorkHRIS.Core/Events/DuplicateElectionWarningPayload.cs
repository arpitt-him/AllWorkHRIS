namespace AllWorkHRIS.Core.Events;

public sealed class DuplicateElectionWarningPayload
{
    public required Guid   EmploymentId       { get; init; }
    public required string DeductionCode      { get; init; }
    public required Guid   ExistingElectionId { get; init; }
}
