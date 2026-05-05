namespace AllWorkHRIS.Module.Benefits.Commands;

public sealed record SuspendElectionCommand
{
    public required Guid ElectionId   { get; init; }
    public required Guid SourceEventId { get; init; }
}
