using System.Text.Json;
using Autofac;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Reporting.Domain;
using AllWorkHRIS.Module.Reporting.Progress;
using AllWorkHRIS.Module.Reporting.Reports;
using AllWorkHRIS.Module.Reporting.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Reporting.Jobs;

/// <summary>
/// Background service that drains queued report execution ids, runs the
/// underlying query, caches the result for UI pickup, and stamps the
/// <c>report_execution_history</c> row with the terminal state. A child
/// lifetime scope is created per execution so scoped services resolve
/// correctly from the singleton hosted service.
/// </summary>
public sealed class ReportGenerationJob : BackgroundService
{
    private readonly ReportJobQueue                _queue;
    private readonly ILifetimeScope                _rootScope;
    private readonly ILogger<ReportGenerationJob> _logger;

    public ReportGenerationJob(
        ReportJobQueue                queue,
        ILifetimeScope                rootScope,
        ILogger<ReportGenerationJob> logger)
    {
        _queue     = queue;
        _rootScope = rootScope;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var executionId in _queue.Channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(executionId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing report execution {ExecutionId}", executionId);
            }
        }
    }

    private async Task ProcessAsync(Guid executionId, CancellationToken ct)
    {
        await using var scope = _rootScope.BeginLifetimeScope();

        var historyRepo  = scope.Resolve<IReportHistoryRepository>();
        var registry     = scope.Resolve<IReportRegistry>();
        var resultCache  = scope.Resolve<IReportResultCache>();
        var notifier     = scope.Resolve<IReportProgressNotifier>();
        var temporal     = scope.Resolve<ITemporalContext>();
        var connFactory  = scope.Resolve<IConnectionFactory>();

        var row = await historyRepo.GetByIdAsync(executionId);
        if (row is null)
        {
            _logger.LogWarning("Execution row {ExecutionId} not found — skipping", executionId);
            return;
        }

        await notifier.UpdateAsync(new ReportProgress
        {
            ExecutionId     = executionId,
            PercentComplete = 5,
            StatusMessage   = "Loading parameters",
            ExecutionStatus = ReportExecutionStatus.Running,
            UpdatedAt       = temporal.GetOperativeNow()
        });

        try
        {
            var parameters = JsonSerializer.Deserialize<ReportParameters>(row.ParametersJson)
                ?? throw new InvalidOperationException("Could not deserialise parameters_json.");

            var query = registry.Get(row.ReportId);
            var asOf  = parameters.AsOfDate
                     ?? DateOnly.FromDateTime(temporal.GetOperativeDate());

            await notifier.UpdateAsync(new ReportProgress
            {
                ExecutionId     = executionId,
                PercentComplete = 25,
                StatusMessage   = $"Running {row.ReportId}",
                ExecutionStatus = ReportExecutionStatus.Running,
                UpdatedAt       = temporal.GetOperativeNow()
            });

            var data = await query.ExecuteAsync(parameters, asOf, ct);

            resultCache.Put(executionId, data);

            using (var uow = new UnitOfWork(connFactory))
            {
                await historyRepo.UpdateCompletedAsync(
                    executionId,
                    rowCount:         data.RowCount,
                    storageReference: null,
                    completedAt:      temporal.GetOperativeNow().UtcDateTime,
                    uow:              uow);
                uow.Commit();
            }

            await notifier.UpdateAsync(new ReportProgress
            {
                ExecutionId     = executionId,
                PercentComplete = 100,
                StatusMessage   = $"{data.RowCount} rows ready",
                ExecutionStatus = ReportExecutionStatus.Completed,
                UpdatedAt       = temporal.GetOperativeNow()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process report execution {ExecutionId}", executionId);

            using (var uow = new UnitOfWork(connFactory))
            {
                await historyRepo.UpdateFailedAsync(
                    executionId,
                    errorMessage: ex.Message,
                    completedAt:  temporal.GetOperativeNow().UtcDateTime,
                    uow:          uow);
                uow.Commit();
            }

            await notifier.UpdateAsync(new ReportProgress
            {
                ExecutionId     = executionId,
                PercentComplete = 100,
                StatusMessage   = "Failed",
                ExecutionStatus = ReportExecutionStatus.Failed,
                ErrorMessage    = ex.Message,
                UpdatedAt       = temporal.GetOperativeNow()
            });
        }
    }
}
