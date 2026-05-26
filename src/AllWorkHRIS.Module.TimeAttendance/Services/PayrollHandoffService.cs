using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.TimeAttendance.Domain;
using AllWorkHRIS.Module.TimeAttendance.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public sealed class PayrollHandoffService : IPayrollHandoffService
{
    private readonly ITimeEntryRepository          _repository;
    private readonly IConnectionFactory            _connectionFactory;
    private readonly ITemporalContext              _temporal;
    private readonly ILogger<PayrollHandoffService> _logger;

    public PayrollHandoffService(
        ITimeEntryRepository           repository,
        IConnectionFactory             connectionFactory,
        ITemporalContext               temporal,
        ILogger<PayrollHandoffService> logger)
    {
        _repository        = repository;
        _connectionFactory = connectionFactory;
        _temporal          = temporal;
        _logger            = logger;
    }

    public async Task<HandoffResult> ExecuteHandoffAsync(
        Guid payrollPeriodId, Guid payrollRunId, CancellationToken ct = default)
    {
        var entries   = (await _repository.GetApprovedForHandoffAsync(payrollPeriodId)).ToList();
        int delivered = 0;
        int failed    = 0;

        // TDO-aware lock timestamp (operative date + real time-of-day), computed once so
        // all entries in this handoff share the same lock instant.
        var lockedAt = _temporal.GetOperativeNow();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            using var uow = new UnitOfWork(_connectionFactory);
            try
            {
                await _repository.LockAsync(entry.TimeEntryId, payrollRunId, lockedAt, uow);
                uow.Commit();
                delivered++;
            }
            catch (Exception ex)
            {
                uow.Rollback();
                failed++;
                _logger.LogError(ex,
                    "Handoff failed for time_entry={TimeEntryId} run={PayrollRunId}",
                    entry.TimeEntryId, payrollRunId);
            }
        }

        var totalHours = delivered > 0
            ? entries.Take(delivered).Sum(e => e.Duration)
            : 0m;

        return new HandoffResult(payrollPeriodId, payrollRunId, delivered, failed, totalHours);
    }
}
