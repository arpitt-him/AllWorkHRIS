namespace AllWorkHRIS.Module.Benefits.Commands;

public sealed record TerminateElectionCommand
{
    public required Guid   ElectionId        { get; init; }
    public required Guid   TerminatedBy      { get; init; }
    public DateOnly?       EffectiveEndDate  { get; init; }
    public string?         TerminationReason { get; init; }
    public Guid?           SourceEventId     { get; init; }
}
