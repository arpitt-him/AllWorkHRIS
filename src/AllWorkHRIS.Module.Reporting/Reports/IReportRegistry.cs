namespace AllWorkHRIS.Module.Reporting.Reports;

/// <summary>
/// Lookup of registered <see cref="IReportQuery"/> instances by report id.
/// Populated by Autofac: every <see cref="IReportQuery"/> implementation
/// registered in <c>ReportingModule</c> is composed into the registry.
/// </summary>
public interface IReportRegistry
{
    IReportQuery              Get(string reportId);
    bool                      Contains(string reportId);
    IEnumerable<IReportQuery> All();
}

public sealed class ReportRegistry : IReportRegistry
{
    private readonly Dictionary<string, IReportQuery> _byId;

    public ReportRegistry(IEnumerable<IReportQuery> queries)
        => _byId = queries.ToDictionary(q => q.ReportId, StringComparer.OrdinalIgnoreCase);

    public IReportQuery Get(string reportId)
        => _byId.TryGetValue(reportId, out var q)
            ? q
            : throw new InvalidOperationException(
                $"No report query registered for id '{reportId}'.");

    public bool Contains(string reportId) => _byId.ContainsKey(reportId);

    public IEnumerable<IReportQuery> All() => _byId.Values;
}
