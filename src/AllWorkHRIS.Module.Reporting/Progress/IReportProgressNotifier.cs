namespace AllWorkHRIS.Module.Reporting.Progress;

/// <summary>
/// Per-execution progress channel for async report jobs. Mirrors
/// <c>IRunProgressNotifier</c> on the Payroll side — in-memory only,
/// polled by the UI (no SignalR yet).
/// </summary>
public interface IReportProgressNotifier
{
    Task            UpdateAsync(ReportProgress progress);
    ReportProgress? GetProgress(Guid executionId);
}
