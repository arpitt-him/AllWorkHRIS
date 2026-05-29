using System.Text.Json;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Jobs;
using AllWorkHRIS.Module.Reporting.Progress;
using AllWorkHRIS.Module.Reporting.Reports;
using AllWorkHRIS.Module.Reporting.Repositories;

namespace AllWorkHRIS.Module.Reporting.Services;

public sealed class ReportService : IReportService
{
    private readonly IConnectionFactory        _connectionFactory;
    private readonly IReportRegistry           _registry;
    private readonly IReportHistoryRepository  _historyRepository;
    private readonly IReportResultCache        _resultCache;
    private readonly ITemporalContext          _temporal;
    private readonly ReportJobQueue            _jobQueue;

    public ReportService(
        IConnectionFactory        connectionFactory,
        IReportRegistry           registry,
        IReportHistoryRepository  historyRepository,
        IReportResultCache        resultCache,
        ITemporalContext          temporal,
        ReportJobQueue            jobQueue)
    {
        _connectionFactory = connectionFactory;
        _registry          = registry;
        _historyRepository = historyRepository;
        _resultCache       = resultCache;
        _temporal          = temporal;
        _jobQueue          = jobQueue;
    }

    public async Task<ReportResult> RunReportAsync(
        string                reportId,
        ReportParameters      parameters,
        Guid                  requestedBy,
        IReadOnlyList<string> userRoles,
        CancellationToken     ct = default)
    {
        var query = _registry.Get(reportId);

        if (!IsAuthorised(query.Definition, userRoles))
            throw new UnauthorizedAccessException(
                $"User does not have access to report {reportId}.");

        var asOf = parameters.AsOfDate
                ?? DateOnly.FromDateTime(_temporal.GetOperativeDate());

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
                StartedAt           = startedAt,
                CreatedTimestamp    = startedAt,
                LastUpdateTimestamp = startedAt
            }, uow);
            uow.Commit();
        }

        try
        {
            var data = await query.ExecuteAsync(parameters, asOf, ct);

            using (var uow = new UnitOfWork(_connectionFactory))
            {
                await _historyRepository.UpdateCompletedAsync(
                    executionId,
                    rowCount:         data.RowCount,
                    storageReference: null,
                    completedAt:      _temporal.GetOperativeNow().UtcDateTime,
                    uow:              uow);
                uow.Commit();
            }

            return ReportResult.Inline(data, executionId);
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

    public async Task<ReportResult> SubmitReportAsync(
        string                reportId,
        ReportParameters      parameters,
        Guid                  requestedBy,
        IReadOnlyList<string> userRoles,
        CancellationToken     ct = default)
    {
        var query = _registry.Get(reportId);

        if (!IsAuthorised(query.Definition, userRoles))
            throw new UnauthorizedAccessException(
                $"User does not have access to report {reportId}.");

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
                ExecutionStatus     = ReportExecutionStatus.AsyncPending,
                ParametersJson      = JsonSerializer.Serialize(parameters),
                AsyncJobId          = executionId,
                StartedAt           = startedAt,
                CreatedTimestamp    = startedAt,
                LastUpdateTimestamp = startedAt
            }, uow);
            uow.Commit();
        }

        if (!_jobQueue.Channel.Writer.TryWrite(executionId))
            throw new InvalidOperationException("Failed to enqueue report job.");

        return ReportResult.Async(jobId: executionId, executionId: executionId);
    }

    public Task<ReportData?> TakeAsyncResultAsync(Guid executionId)
        => Task.FromResult(_resultCache.TakeOnce(executionId));

    public Task<IEnumerable<ReportDefinition>> GetAvailableReportsAsync(IReadOnlyList<string> userRoles)
        => Task.FromResult(_registry.All()
            .Select(q => q.Definition)
            .Where(d => IsAuthorised(d, userRoles)));

    public Task<IEnumerable<ReportExecutionHistory>> GetRecentExecutionsAsync(Guid requestedBy, int count = 20)
        => _historyRepository.GetRecentByUserAsync(requestedBy, count);

    public Task<IEnumerable<ReportExecutionHistory>> GetRecentForReportAsync(Guid requestedBy, string reportId, int count = 5)
        => _historyRepository.GetRecentByUserAndReportAsync(requestedBy, reportId, count);

    private static bool IsAuthorised(ReportDefinition def, IReadOnlyList<string> userRoles)
        => def.AllowedRoles.Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
}
