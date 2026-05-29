using AllWorkHRIS.Module.Reporting.Domain;

namespace AllWorkHRIS.Module.Reporting.Reports;

/// <summary>
/// Contract every report query implements. The query owns its catalog
/// metadata (<see cref="Definition"/>) and the SQL fetch. The service layer
/// handles auth, scope enforcement, history, and async dispatch.
/// </summary>
public interface IReportQuery
{
    string           ReportId   { get; }
    ReportDefinition Definition { get; }

    Task<ReportData> ExecuteAsync(
        ReportParameters parameters,
        DateOnly         asOf,
        CancellationToken ct);

    /// <summary>
    /// Returns a human-readable, one-line summary of the parameters used for this
    /// run — surfaced in the XLSX title block (row 2) and the PDF header. Reports
    /// that have no meaningful per-execution params can leave the default empty.
    /// </summary>
    Task<string> GetParameterSummaryAsync(
        ReportParameters parameters,
        CancellationToken ct)
        => Task.FromResult(string.Empty);
}
