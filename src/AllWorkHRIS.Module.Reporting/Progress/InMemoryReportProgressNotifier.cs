using System.Collections.Concurrent;

namespace AllWorkHRIS.Module.Reporting.Progress;

/// <summary>
/// Singleton in-memory <see cref="IReportProgressNotifier"/>. Mirrors the
/// Payroll module's polling-based progress notifier — survives only as long
/// as the host process. Sufficient for M1; replaced when a platform-wide
/// SignalR / persisted-job-state model lands.
/// </summary>
public sealed class InMemoryReportProgressNotifier : IReportProgressNotifier
{
    private readonly ConcurrentDictionary<Guid, ReportProgress> _state = new();

    public Task UpdateAsync(ReportProgress progress)
    {
        _state[progress.ExecutionId] = progress;
        return Task.CompletedTask;
    }

    public ReportProgress? GetProgress(Guid executionId)
        => _state.TryGetValue(executionId, out var p) ? p : null;
}
