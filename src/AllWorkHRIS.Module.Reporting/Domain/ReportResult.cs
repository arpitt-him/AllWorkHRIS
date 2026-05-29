namespace AllWorkHRIS.Module.Reporting.Domain;

/// <summary>
/// Result of <see cref="Services.IReportService.RunReportAsync"/>. Either inline
/// data (small result sets) or an async job handle (result set exceeded the
/// async threshold). Every variant carries the execution-history row id so the
/// caller can correlate downstream events.
/// </summary>
public sealed record ReportResult(
    bool        IsAsync,
    ReportData? Data,
    Guid?       JobId,
    Guid        ExecutionId)
{
    public static ReportResult Inline(ReportData data, Guid executionId)
        => new(false, data, null, executionId);

    public static ReportResult Async(Guid jobId, Guid executionId)
        => new(true, null, jobId, executionId);
}
