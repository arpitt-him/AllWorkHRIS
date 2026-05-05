using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Module.TimeAttendance.Domain;
using AllWorkHRIS.Module.TimeAttendance.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.TimeAttendance.Events;

public sealed class TimeAttendanceEventSubscriber : IEventSubscriber
{
    private readonly ITimeEntryRepository                   _repository;
    private readonly IConnectionFactory                     _connectionFactory;
    private readonly ILookupCache                           _lookupCache;
    private readonly ILogger<TimeAttendanceEventSubscriber> _logger;

    public TimeAttendanceEventSubscriber(
        ITimeEntryRepository                   repository,
        IConnectionFactory                     connectionFactory,
        ILookupCache                           lookupCache,
        ILogger<TimeAttendanceEventSubscriber> logger)
    {
        _repository        = repository;
        _connectionFactory = connectionFactory;
        _lookupCache       = lookupCache;
        _logger            = logger;
    }

    public void RegisterHandlers(IEventPublisher eventPublisher)
    {
        eventPublisher.RegisterHandler<TerminationEventPayload>(HandleTerminationAsync);
    }

    private async Task HandleTerminationAsync(TerminationEventPayload payload)
    {
        _logger.LogInformation(
            "T&A — closing open entries for employment={EmploymentId}",
            payload.EmploymentId);

        var openEntries = (await _repository.GetOpenByEmploymentAsync(payload.EmploymentId)).ToList();

        if (openEntries.Count == 0) return;

        var voidId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, "VOID");

        using var conn = _connectionFactory.CreateConnection();
        foreach (var entry in openEntries)
        {
            try
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE time_entry
                    SET    status_id        = @StatusId,
                           rejection_reason = 'Closed due to employment termination',
                           updated_at       = @Now
                    WHERE  time_entry_id    = @TimeEntryId
                    """,
                    new { StatusId = voidId, Now = DateTimeOffset.UtcNow, TimeEntryId = entry.TimeEntryId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to void time entry {TimeEntryId} during termination for employment {EmploymentId}",
                    entry.TimeEntryId, payload.EmploymentId);
            }
        }
    }
}
