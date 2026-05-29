using System.Text.Json;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Export;
using AllWorkHRIS.Module.Reporting.Reports;
using AllWorkHRIS.Module.Reporting.Repositories;

namespace AllWorkHRIS.Module.Reporting.Services;

public sealed class ReportExportService : IReportExportService
{
    private readonly IConnectionFactory       _connectionFactory;
    private readonly IReportRegistry          _registry;
    private readonly IReportHistoryRepository _historyRepository;
    private readonly ITemporalContext         _temporal;
    private readonly CsvExporter              _csv;
    private readonly XlsxExporter             _xlsx;
    private readonly PdfExporter              _pdf;

    public ReportExportService(
        IConnectionFactory       connectionFactory,
        IReportRegistry          registry,
        IReportHistoryRepository historyRepository,
        ITemporalContext         temporal,
        CsvExporter              csv,
        XlsxExporter             xlsx,
        PdfExporter              pdf)
    {
        _connectionFactory = connectionFactory;
        _registry          = registry;
        _historyRepository = historyRepository;
        _temporal          = temporal;
        _csv               = csv;
        _xlsx              = xlsx;
        _pdf               = pdf;
    }

    public Task<ExportResult> ExportCsvAsync(string reportId, ReportParameters parameters, Guid requestedBy, IReadOnlyList<string> userRoles, CancellationToken ct = default)
        => ExportAsync(reportId, parameters, requestedBy, userRoles, "CSV",  "text/csv",                                                                  ".csv",  ct);

    public Task<ExportResult> ExportXlsxAsync(string reportId, ReportParameters parameters, Guid requestedBy, IReadOnlyList<string> userRoles, CancellationToken ct = default)
        => ExportAsync(reportId, parameters, requestedBy, userRoles, "XLSX", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",        ".xlsx", ct);

    public Task<ExportResult> ExportPdfAsync(string reportId, ReportParameters parameters, Guid requestedBy, IReadOnlyList<string> userRoles, CancellationToken ct = default)
        => ExportAsync(reportId, parameters, requestedBy, userRoles, "PDF",  "application/pdf",                                                            ".pdf",  ct);

    private async Task<ExportResult> ExportAsync(
        string                reportId,
        ReportParameters      parameters,
        Guid                  requestedBy,
        IReadOnlyList<string> userRoles,
        string                format,
        string                mimeType,
        string                extension,
        CancellationToken     ct)
    {
        var query = _registry.Get(reportId);

        if (!IsAuthorised(query.Definition, userRoles))
            throw new UnauthorizedAccessException(
                $"User does not have access to report {reportId}.");

        var asOf        = parameters.AsOfDate ?? DateOnly.FromDateTime(_temporal.GetOperativeDate());
        var executionId = Guid.NewGuid();
        var startedAt   = _temporal.GetOperativeNow().UtcDateTime;

        using (var uow = new UnitOfWork(_connectionFactory))
        {
            await _historyRepository.InsertAsync(new ReportExecutionHistory
            {
                ExecutionId         = executionId,
                ReportId            = reportId,
                ReportTitle         = query.Definition.Title,
                RequestedBy         = requestedBy,
                ExecutionStatus     = ReportExecutionStatus.Running,
                ParametersJson      = JsonSerializer.Serialize(parameters),
                ExportFormat        = format,
                StartedAt           = startedAt,
                CreatedTimestamp    = startedAt,
                LastUpdateTimestamp = startedAt
            }, uow);
            uow.Commit();
        }

        try
        {
            var data             = await query.ExecuteAsync(parameters, asOf, ct);
            var paramSummary     = await query.GetParameterSummaryAsync(parameters, ct);
            var generatedAtLocal = _temporal.GetOperativeNow().LocalDateTime;

            Stream content = format switch
            {
                "CSV"  => await _csv.ExportAsync(data, ct),
                "XLSX" => _xlsx.Export(data, query.Definition, paramSummary, generatedAtLocal),
                "PDF"  => _pdf .Export(data, query.Definition, paramSummary, generatedAtLocal),
                _      => throw new ArgumentException($"Unsupported export format '{format}'.")
            };

            using (var uow = new UnitOfWork(_connectionFactory))
            {
                await _historyRepository.UpdateCompletedAsync(
                    executionId,
                    rowCount:         data.RowCount,
                    storageReference: null,    // M1: not retained (deferred to async/cleanup work)
                    completedAt:      _temporal.GetOperativeNow().UtcDateTime,
                    uow:              uow);
                uow.Commit();
            }

            var fileName = $"{reportId}_{generatedAtLocal:yyyyMMdd_HHmm}{extension}";
            return new ExportResult(content, mimeType, fileName, executionId);
        }
        catch (Exception ex)
        {
            using var uow = new UnitOfWork(_connectionFactory);
            await _historyRepository.UpdateFailedAsync(
                executionId,
                errorMessage: ex.Message,
                completedAt:  _temporal.GetOperativeNow().UtcDateTime,
                uow:          uow);
            uow.Commit();
            throw;
        }
    }

    private static bool IsAuthorised(ReportDefinition def, IReadOnlyList<string> userRoles)
        => def.AllowedRoles.Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
}
