namespace AllWorkHRIS.Module.Reporting.Progress;

/// <summary>
/// Snapshot of an async report job's state. Posted by
/// <see cref="ReportGenerationJob"/> after each lifecycle stage and read by
/// the UI's polling loop in <c>ReportRunner.razor</c>.
/// </summary>
public sealed record ReportProgress
{
    public required Guid           ExecutionId      { get; init; }
    public required int            PercentComplete  { get; init; }
    public required string         StatusMessage    { get; init; }
    public required string         ExecutionStatus  { get; init; }   // RUNNING | COMPLETED | FAILED
    public          string?        StorageReference { get; init; }
    public          string?        ErrorMessage     { get; init; }
    public required DateTimeOffset UpdatedAt        { get; init; }
}
