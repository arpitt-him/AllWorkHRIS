using System.Collections.Concurrent;
using AllWorkHRIS.Core.Events;

namespace AllWorkHRIS.Host.Hubs;

public sealed class RunProgressNotifier : IRunProgressNotifier
{
    private readonly ConcurrentDictionary<Guid, RunProgress> _cache = new();

    public Task UpdateAsync(RunProgress progress)
    {
        _cache[progress.RunId] = progress;
        return Task.CompletedTask;
    }

    public RunProgress? GetProgress(Guid runId)
        => _cache.GetValueOrDefault(runId);
}
