namespace AllWorkHRIS.Module.Reporting.Domain;

/// <summary>
/// One row in <c>report_execution_history</c>. Written at execution start
/// (status = RUNNING) and updated on terminal state via the repository's
/// UpdateCompleted / UpdateFailed / UpdateAsyncCompleted methods — the
/// record itself is immutable.
/// </summary>
public sealed record ReportExecutionHistory
{
    public required Guid     ExecutionId         { get; init; }
    public required string   ReportId            { get; init; }
    public required string   ReportTitle         { get; init; }
    public required Guid     RequestedBy         { get; init; }
    public required string   ExecutionStatus     { get; init; }
    public required string   ParametersJson      { get; init; }
    public int?              RowCount            { get; init; }
    public string?           ExportFormat        { get; init; }
    public string?           StorageReference    { get; init; }
    public Guid?             AsyncJobId          { get; init; }
    public required DateTime StartedAt           { get; init; }
    public DateTime?         CompletedAt         { get; init; }
    public string?           ErrorMessage        { get; init; }

    // Project-wide audit columns. CreatedTimestamp is set once at insert
    // and never changed; LastUpdateTimestamp bumps on every status transition.
    public required DateTime CreatedTimestamp    { get; init; }
    public required DateTime LastUpdateTimestamp { get; init; }
}

/// <summary>Status constants for <see cref="ReportExecutionHistory.ExecutionStatus"/>.</summary>
public static class ReportExecutionStatus
{
    public const string Running      = "RUNNING";
    public const string Completed    = "COMPLETED";
    public const string Failed       = "FAILED";
    public const string AsyncPending = "ASYNC_PENDING";
}
