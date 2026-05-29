using System.Collections.Concurrent;
using AllWorkHRIS.Module.Reporting.Domain;

namespace AllWorkHRIS.Module.Reporting.Progress;

/// <summary>
/// Volatile in-memory cache holding the <see cref="ReportData"/> for async
/// executions until the UI fetches it. M1 simplification — when async
/// retention is built out, results will materialise to
/// <c>IDocumentStorageService</c> and this cache goes away. Cache TTL is
/// process lifetime; entries are removed on first successful fetch.
/// </summary>
public interface IReportResultCache
{
    void        Put(Guid executionId, ReportData data);
    ReportData? TakeOnce(Guid executionId);
}

public sealed class InMemoryReportResultCache : IReportResultCache
{
    private readonly ConcurrentDictionary<Guid, ReportData> _entries = new();

    public void Put(Guid executionId, ReportData data)
        => _entries[executionId] = data;

    public ReportData? TakeOnce(Guid executionId)
        => _entries.TryRemove(executionId, out var data) ? data : null;
}
