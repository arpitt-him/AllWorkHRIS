using AllWorkHRIS.Module.Reporting.Domain;

namespace AllWorkHRIS.Module.Reporting.Services;

public interface IReportExportService
{
    Task<ExportResult> ExportCsvAsync(
        string                reportId,
        ReportParameters      parameters,
        Guid                  requestedBy,
        IReadOnlyList<string> userRoles,
        CancellationToken     ct = default);

    Task<ExportResult> ExportXlsxAsync(
        string                reportId,
        ReportParameters      parameters,
        Guid                  requestedBy,
        IReadOnlyList<string> userRoles,
        CancellationToken     ct = default);

    Task<ExportResult> ExportPdfAsync(
        string                reportId,
        ReportParameters      parameters,
        Guid                  requestedBy,
        IReadOnlyList<string> userRoles,
        CancellationToken     ct = default);
}
