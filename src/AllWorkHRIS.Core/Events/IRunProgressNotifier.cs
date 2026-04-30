namespace AllWorkHRIS.Core.Events;

public sealed record RunProgress
{
    public required Guid           RunId           { get; init; }
    public required int            PercentComplete { get; init; }
    public required int            Processed       { get; init; }
    public required int            Total           { get; init; }
    public required int            Failed          { get; init; }
    public required string         StatusMessage   { get; init; }
    public required string         RunStatus       { get; init; }
    public required DateTimeOffset UpdatedAt       { get; init; }
}

public interface IRunProgressNotifier
{
    Task         UpdateAsync(RunProgress progress);
    RunProgress? GetProgress(Guid runId);
}
