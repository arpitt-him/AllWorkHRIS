using AllWorkHRIS.Module.Reporting.Domain;

namespace AllWorkHRIS.Module.Reporting.Services;

public interface IReportService
{
    /// <summary>
    /// Runs a report inline (synchronously). Writes a
    /// <see cref="ReportExecutionHistory"/> row before execution and updates
    /// it on terminal state. M1 always returns an inline result; the
    /// auto-async-by-threshold path is deferred (see ToDo).
    /// </summary>
    Task<ReportResult> RunReportAsync(
        string                reportId,
        ReportParameters      parameters,
        Guid                  requestedBy,
        IReadOnlyList<string> userRoles,
        CancellationToken     ct = default);

    /// <summary>
    /// Submits a report to the async generation job and returns immediately
    /// with a job-id ReportResult. The UI polls
    /// <see cref="Progress.IReportProgressNotifier"/> for completion, then
    /// calls <see cref="TakeAsyncResultAsync"/> to fetch the data once.
    /// </summary>
    Task<ReportResult> SubmitReportAsync(
        string                reportId,
        ReportParameters      parameters,
        Guid                  requestedBy,
        IReadOnlyList<string> userRoles,
        CancellationToken     ct = default);

    /// <summary>
    /// One-shot fetch of an async result. Removes the entry from the cache;
    /// callers must retain the data themselves if they need it later.
    /// </summary>
    Task<ReportData?> TakeAsyncResultAsync(Guid executionId);

    Task<IEnumerable<ReportDefinition>> GetAvailableReportsAsync(IReadOnlyList<string> userRoles);

    Task<IEnumerable<ReportExecutionHistory>> GetRecentExecutionsAsync(Guid requestedBy, int count = 20);

    Task<IEnumerable<ReportExecutionHistory>> GetRecentForReportAsync(Guid requestedBy, string reportId, int count = 5);
}
