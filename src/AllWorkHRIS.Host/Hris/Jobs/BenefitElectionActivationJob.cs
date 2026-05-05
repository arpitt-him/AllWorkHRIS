using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Benefits.Repositories;

namespace AllWorkHRIS.Host.Hris.Jobs;

public sealed class BenefitElectionActivationJob : BackgroundService
{
    private readonly IServiceScopeFactory                  _scopeFactory;
    private readonly ITemporalOverrideService              _overrideService;
    private readonly ILogger<BenefitElectionActivationJob> _logger;

    private DateOnly?     _lastRunDate;
    private volatile bool _tdo;

    public BenefitElectionActivationJob(
        IServiceScopeFactory                  scopeFactory,
        ITemporalOverrideService              overrideService,
        ILogger<BenefitElectionActivationJob> logger)
    {
        _scopeFactory    = scopeFactory;
        _overrideService = overrideService;
        _logger          = logger;

        _overrideService.OnChanged += () => _tdo = true;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _tdo = false;

            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "BenefitElectionActivationJob cycle failed.");
            }

            var deadline = DateTime.UtcNow.AddHours(24);
            while (!ct.IsCancellationRequested && !_tdo && DateTime.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope    = _scopeFactory.CreateAsyncScope();
        var temporal             = scope.ServiceProvider.GetRequiredService<ITemporalContext>();
        var electionRepo         = scope.ServiceProvider.GetRequiredService<IBenefitElectionRepository>();

        var today = DateOnly.FromDateTime(temporal.GetOperativeDate());

        if (_lastRunDate.HasValue && today <= _lastRunDate.Value)
        {
            _logger.LogDebug(
                "BenefitElectionActivationJob: skipping cycle — operative date {Today} has not advanced past last run {Last}.",
                today, _lastRunDate.Value);
            return;
        }

        var activated = await electionRepo.ActivatePendingAsync(today, ct);

        _lastRunDate = today;

        if (activated > 0)
            _logger.LogInformation(
                "BenefitElectionActivationJob: activated {Count} pending election(s) as of {Date}.",
                activated, today);
    }
}
