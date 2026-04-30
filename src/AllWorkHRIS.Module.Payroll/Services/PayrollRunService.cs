using System.Threading.Channels;
using AllWorkHRIS.Module.Payroll.Commands;
using AllWorkHRIS.Module.Payroll.Domain.Run;
using AllWorkHRIS.Module.Payroll.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class PayrollRunService : IPayrollRunService
{
    private readonly IPayrollRunRepository      _runRepo;
    private readonly IPayrollContextRepository  _contextRepo;
    private readonly Channel<Guid>              _queue;
    private readonly ILogger<PayrollRunService> _logger;

    public PayrollRunService(
        IPayrollRunRepository      runRepo,
        IPayrollContextRepository  contextRepo,
        Channel<Guid>              queue,
        ILogger<PayrollRunService> logger)
    {
        _runRepo     = runRepo;
        _contextRepo = contextRepo;
        _queue       = queue;
        _logger      = logger;
    }

    public async Task<Guid> InitiateRunAsync(InitiatePayrollRunCommand command)
    {
        var hasOpen = await _runRepo.HasOpenRunForPeriodAsync(command.PayrollContextId, command.PeriodId);
        if (hasOpen)
            throw new InvalidOperationException(
                $"An open payroll run already exists for period {command.PeriodId}.");

        var period = await _contextRepo.GetPeriodByIdAsync(command.PeriodId)
            ?? throw new InvalidOperationException($"Payroll period {command.PeriodId} not found.");

        var now = DateTimeOffset.UtcNow;
        var run = new PayrollRun
        {
            RunId                      = Guid.NewGuid(),
            PayrollContextId           = command.PayrollContextId,
            PeriodId                   = command.PeriodId,
            PayDate                    = period.PayDate,
            RunTypeId                  = command.RunTypeId,
            RunStatusId                = (int)PayrollRunStatus.Draft,
            RunDescription             = command.RunDescription,
            ParentRunId                = command.ParentRunId,
            RelatedRunGroupId          = null,
            RuleAndConfigVersionRef    = null,
            TemporalOverrideActiveFlag = false,
            TemporalOverrideDate       = null,
            InitiatedBy                = command.InitiatedBy,
            RunStartTimestamp          = null,
            RunEndTimestamp            = null,
            CreatedBy                  = command.InitiatedBy,
            CreationTimestamp          = now,
            LastUpdatedBy              = command.InitiatedBy,
            LastUpdateTimestamp        = now
        };

        await _runRepo.InsertAsync(run);
        _queue.Writer.TryWrite(run.RunId);

        _logger.LogInformation(
            "Payroll run {RunId} initiated for context {ContextId} period {PeriodId}",
            run.RunId, run.PayrollContextId, run.PeriodId);

        return run.RunId;
    }

    public async Task ApproveRunAsync(ApprovePayrollRunCommand command)
    {
        var run = await RequireRunAsync(command.RunId);
        RequireStatus(run, PayrollRunStatus.Calculated, "approve");
        await _runRepo.UpdateStatusAsync(command.RunId, (int)PayrollRunStatus.Approved, command.ApprovedBy);
        _logger.LogInformation("Run {RunId} approved by {UserId}", command.RunId, command.ApprovedBy);
    }

    public async Task ReleaseRunAsync(ReleasePayrollRunCommand command)
    {
        var run = await RequireRunAsync(command.RunId);
        RequireStatus(run, PayrollRunStatus.Approved, "release");
        await _runRepo.UpdateStatusAsync(command.RunId, (int)PayrollRunStatus.Releasing, command.ReleasedBy);
        _queue.Writer.TryWrite(command.RunId);
        _logger.LogInformation("Run {RunId} release initiated by {UserId}", command.RunId, command.ReleasedBy);
    }

    public async Task CancelRunAsync(CancelPayrollRunCommand command)
    {
        var run = await RequireRunAsync(command.RunId);

        if (run.RunStatusId != (int)PayrollRunStatus.Draft &&
            run.RunStatusId != (int)PayrollRunStatus.Calculated)
            throw new InvalidOperationException(
                $"Run {command.RunId} cannot be cancelled from status {run.RunStatusId}.");

        await _runRepo.UpdateStatusAsync(command.RunId, (int)PayrollRunStatus.Cancelled, command.CancelledBy);
        _logger.LogInformation("Run {RunId} cancelled by {UserId}: {Reason}",
            command.RunId, command.CancelledBy, command.Reason);
    }

    public Task<PayrollRun?> GetRunByIdAsync(Guid runId)
        => _runRepo.GetByIdAsync(runId);

    public Task<IReadOnlyList<PayrollRun>> GetRunsByContextAsync(Guid payrollContextId)
        => _runRepo.GetByContextAsync(payrollContextId);

    private async Task<PayrollRun> RequireRunAsync(Guid runId)
        => await _runRepo.GetByIdAsync(runId)
           ?? throw new InvalidOperationException($"Payroll run {runId} not found.");

    private static void RequireStatus(PayrollRun run, PayrollRunStatus required, string action)
    {
        if (run.RunStatusId != (int)required)
            throw new InvalidOperationException(
                $"Run {run.RunId} must be in {required} state to {action}. Current: {run.RunStatusId}");
    }
}
