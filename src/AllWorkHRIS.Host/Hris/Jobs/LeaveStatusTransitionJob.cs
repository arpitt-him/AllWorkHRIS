using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host.Hris.Repositories;

namespace AllWorkHRIS.Host.Hris.Jobs;

public sealed class LeaveStatusTransitionJob : BackgroundService
{
    private readonly IServiceScopeFactory              _scopeFactory;
    private readonly ITemporalOverrideService          _overrideService;
    private readonly ILogger<LeaveStatusTransitionJob> _logger;

    private DateOnly?     _lastRunDate;
    private volatile bool _tdo;

    public LeaveStatusTransitionJob(
        IServiceScopeFactory              scopeFactory,
        ITemporalOverrideService          overrideService,
        ILogger<LeaveStatusTransitionJob> logger)
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
                _logger.LogError(ex, "LeaveStatusTransitionJob cycle failed.");
            }

            var deadline = DateTime.UtcNow.AddHours(24);
            while (!ct.IsCancellationRequested && !_tdo && DateTime.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope   = _scopeFactory.CreateAsyncScope();
        var connectionFactory   = scope.ServiceProvider.GetRequiredService<IConnectionFactory>();
        var lookupCache         = scope.ServiceProvider.GetRequiredService<ILookupCache>();
        var temporalContext     = scope.ServiceProvider.GetRequiredService<ITemporalContext>();
        var leaveRepo           = scope.ServiceProvider.GetRequiredService<ILeaveRequestRepository>();

        var operativeDate = DateOnly.FromDateTime(temporalContext.GetOperativeDate());

        if (_lastRunDate.HasValue && operativeDate <= _lastRunDate.Value)
        {
            _logger.LogDebug(
                "LeaveStatusTransitionJob: skipping cycle — operative date {Today} has not advanced past last run {Last}.",
                operativeDate, _lastRunDate.Value);
            return;
        }

        var inProgressId  = lookupCache.GetId(LookupTables.LeaveStatus, "IN_PROGRESS");
        var systemActorId = Guid.Empty;

        var approvedRequests = await leaveRepo.GetByStatusAsync("APPROVED");

        foreach (var req in approvedRequests.Where(r => r.LeaveStartDate <= operativeDate))
        {
            try
            {
                using var uow = new UnitOfWork(connectionFactory);
                try
                {
                    await leaveRepo.UpdateStatusAsync(
                        req.LeaveRequestId, inProgressId, systemActorId, uow);
                    uow.Commit();
                    _logger.LogInformation(
                        "Leave {Id} transitioned to IN_PROGRESS (start {Date}).",
                        req.LeaveRequestId, req.LeaveStartDate);
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transition leave {Id}.", req.LeaveRequestId);
            }
        }

        _lastRunDate = operativeDate;
    }
}
