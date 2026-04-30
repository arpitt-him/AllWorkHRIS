using System.Threading.Channels;
using AllWorkHRIS.Module.Payroll.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll.Jobs;

/// <summary>
/// Long-running background service that dequeues payroll run IDs and
/// drives each through the full calculation lifecycle.
/// Uses Channel&lt;Guid&gt; so runs are processed one-at-a-time per instance
/// without blocking the web request thread.
/// </summary>
public sealed class PayrollRunJob : BackgroundService
{
    private readonly Channel<Guid>        _queue;
    private readonly IPayrollRunService   _runService;
    private readonly ICalculationEngine   _calculationEngine;
    private readonly ILogger<PayrollRunJob> _logger;

    public PayrollRunJob(
        Channel<Guid>          queue,
        IPayrollRunService     runService,
        ICalculationEngine     calculationEngine,
        ILogger<PayrollRunJob> logger)
    {
        _queue             = queue;
        _runService        = runService;
        _calculationEngine = calculationEngine;
        _logger            = logger;
    }

    public static void Enqueue(Channel<Guid> queue, Guid runId)
        => queue.Writer.TryWrite(runId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessRunAsync(runId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing payroll run {RunId}", runId);
            }
        }
    }

    private async Task ProcessRunAsync(Guid runId, CancellationToken ct)
    {
        _logger.LogInformation("Starting calculation for run {RunId}", runId);

        var run = await _runService.GetRunByIdAsync(runId);
        if (run is null)
        {
            _logger.LogWarning("Run {RunId} not found — skipping", runId);
            return;
        }

        // TODO: transition run to CALCULATING, iterate employee population,
        // call ICalculationEngine.CalculateAsync per employee,
        // publish SignalR progress updates, finalize run status
        await Task.CompletedTask;

        _logger.LogInformation("Calculation complete for run {RunId}", runId);
    }
}
