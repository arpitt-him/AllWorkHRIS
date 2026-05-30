using System.Text.Json;
using System.Threading.Channels;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Payroll.Commands;
using AllWorkHRIS.Module.Payroll.Domain.Run;
using AllWorkHRIS.Module.Payroll.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class PayrollRunService : IPayrollRunService
{
    // IEmployeePayrollResultRepository and IAccumulatorService were dependencies
    // of the old cancel-from-Calculated reversal path. ADR-017 (Phase 12.5.3)
    // removed that path — cancel-from-Calculated is now a clean discard since
    // calculation no longer posts to the ledger. Re-add these deps if/when an
    // explicit "reverse an approved run" surface is built (separate from cancel).
    private readonly IPayrollRunRepository       _runRepo;
    private readonly IPayrollContextRepository   _contextRepo;
    private readonly Channel<Guid>               _queue;
    private readonly ITemporalContext            _temporal;
    private readonly ILogger<PayrollRunService>  _logger;
    private readonly IAuditService               _auditService;
    private readonly ILookupCache                _lookup;

    public PayrollRunService(
        IPayrollRunRepository       runRepo,
        IPayrollContextRepository   contextRepo,
        Channel<Guid>               queue,
        ITemporalContext            temporal,
        ILogger<PayrollRunService>  logger,
        IAuditService               auditService,
        ILookupCache                lookup)
    {
        _runRepo      = runRepo;
        _contextRepo  = contextRepo;
        _queue        = queue;
        _temporal     = temporal;
        _logger       = logger;
        _auditService = auditService;
        _lookup       = lookup;
    }

    // Status-id helpers — keep the cache call out of the hot path and give the
    // call sites a readable verb.
    private int StatusId(string code) => _lookup.GetId(LookupTables.RunStatus, code);

    public async Task<Guid> InitiateRunAsync(InitiatePayrollRunCommand command)
    {
        // ADR-017 §4b/§5: block only on an un-approved in-flight run for the
        // same payroll context (per context, not per period — supplemental
        // runs for a period that already has an approved run are explicitly
        // allowed). The block message names the in-flight run so the operator
        // knows what to clear.
        var inFlight = await _runRepo.GetInFlightRunForContextAsync(command.PayrollContextId);
        if (inFlight is not null)
        {
            var inFlightStatus = _lookup.GetCode(LookupTables.RunStatus, inFlight.RunStatusId);
            var desc           = string.IsNullOrWhiteSpace(inFlight.RunDescription)
                ? $"run {inFlight.RunId}"
                : $"run \"{inFlight.RunDescription}\" ({inFlight.RunId})";
            throw new InvalidOperationException(
                $"Cannot initiate a new run — an in-flight {inFlightStatus} {desc} for this payroll context " +
                $"must be approved or cancelled first.");
        }

        // ADR-017 §4b: a period gets exactly one Regular (scheduled) run. The
        // post-approval allowance is for Supplemental / Adjustment / Correction
        // catch-up runs — a second *Regular* run for a period that already has
        // one is a duplicate, not a catch-up, so block it regardless of the
        // existing run's status (a cancelled/failed regular run does not count).
        if (command.RunTypeId == _lookup.GetId(LookupTables.RunType, "REGULAR"))
        {
            var existingRegular = await _runRepo.GetActiveRegularRunForPeriodAsync(command.PeriodId);
            if (existingRegular is not null)
            {
                var existingStatus = _lookup.GetCode(LookupTables.RunStatus, existingRegular.RunStatusId);
                var existingDesc   = string.IsNullOrWhiteSpace(existingRegular.RunDescription)
                    ? $"run {existingRegular.RunId}"
                    : $"run \"{existingRegular.RunDescription}\" ({existingRegular.RunId})";
                throw new InvalidOperationException(
                    $"Cannot initiate a second Regular run — {existingDesc} ({existingStatus}) already covers " +
                    $"this period. Use a Supplemental or Correction run to add to an already-approved period.");
            }
        }

        var period = await _contextRepo.GetPeriodByIdAsync(command.PeriodId)
            ?? throw new InvalidOperationException($"Payroll period {command.PeriodId} not found.");

        var now = _temporal.GetOperativeNow();
        var run = new PayrollRun
        {
            RunId                      = Guid.NewGuid(),
            PayrollContextId           = command.PayrollContextId,
            PeriodId                   = command.PeriodId,
            PayDate                    = period.PayDate,
            RunTypeId                  = command.RunTypeId,
            RunStatusId                = StatusId("DRAFT"),
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

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:       "CREATE",
            EntityType:      "PayrollRun",
            EntityId:        run.RunId,
            ModuleName:      "PAYROLL",
            ChangeSummary:   $"Payroll run initiated for context {run.PayrollContextId} period {run.PeriodId}",
            ParentEntityType: "PayrollContext",
            ParentEntityId:  run.PayrollContextId,
            AfterJson:       JsonSerializer.Serialize(new { run_status = "DRAFT", run.PeriodId })
        ));

        return run.RunId;
    }

    public async Task ApproveRunAsync(ApprovePayrollRunCommand command)
    {
        // ADR-017 (Phase 12.5.3): approval is now the YTD-commit point.
        // Flip to APPROVING and enqueue; PayrollRunJob's approve branch posts
        // accumulator impacts for each succeeded result then flips to APPROVED.
        // This mirrors the existing Releasing → Released backgrounding pattern.
        var run = await RequireRunAsync(command.RunId);
        RequireStatus(run, "CALCULATED", "approve");
        await _runRepo.UpdateStatusAsync(command.RunId, StatusId("APPROVING"), command.ApprovedBy);
        _queue.Writer.TryWrite(command.RunId);
        _logger.LogInformation("Run {RunId} approval initiated by {UserId} — posting accumulators in background",
            command.RunId, command.ApprovedBy);

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:       "STATUS_CHANGE",
            EntityType:      "PayrollRun",
            EntityId:        command.RunId,
            ModuleName:      "PAYROLL",
            ChangeSummary:   $"Payroll run approval initiated (accumulator post backgrounded)",
            ParentEntityType: "PayrollContext",
            ParentEntityId:  run.PayrollContextId,
            AfterJson:       JsonSerializer.Serialize(new { run_status = "APPROVING" })
        ));
    }

    public async Task ReleaseRunAsync(ReleasePayrollRunCommand command)
    {
        var run = await RequireRunAsync(command.RunId);
        RequireStatus(run, "APPROVED", "release");
        await _runRepo.UpdateStatusAsync(command.RunId, StatusId("RELEASING"), command.ReleasedBy);
        _queue.Writer.TryWrite(command.RunId);
        _logger.LogInformation("Run {RunId} release initiated by {UserId}", command.RunId, command.ReleasedBy);
    }

    public async Task CancelRunAsync(CancelPayrollRunCommand command)
    {
        // ADR-017 (Phase 12.5.3): cancel-from-Calculated is now a clean
        // discard — calculation no longer posts accumulator impacts, so
        // there is nothing to reverse. The IAccumulatorService.ReverseAsync
        // machinery is retained but repurposed for the future "reverse an
        // approved run" flow (not built this phase). Cancel-from-Draft
        // remains unchanged. All other statuses reject.
        var run = await RequireRunAsync(command.RunId);

        var draftId      = StatusId("DRAFT");
        var calculatedId = StatusId("CALCULATED");

        if (run.RunStatusId != draftId && run.RunStatusId != calculatedId)
        {
            var currentCode = _lookup.GetCode(LookupTables.RunStatus, run.RunStatusId);
            throw new InvalidOperationException(
                $"Run {command.RunId} cannot be cancelled from status {currentCode}.");
        }

        await _runRepo.UpdateStatusAsync(command.RunId, StatusId("CANCELLED"), command.CancelledBy);
        _logger.LogInformation("Run {RunId} cancelled by {UserId}: {Reason}",
            command.RunId, command.CancelledBy, command.Reason);

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:       "STATUS_CHANGE",
            EntityType:      "PayrollRun",
            EntityId:        command.RunId,
            ModuleName:      "PAYROLL",
            ChangeSummary:   $"Payroll run cancelled: {command.Reason}",
            ParentEntityType: "PayrollContext",
            ParentEntityId:  run.PayrollContextId,
            AfterJson:       JsonSerializer.Serialize(new { run_status = "CANCELLED", reason = command.Reason })
        ));
    }

    public Task<PayrollRun?> GetRunByIdAsync(Guid runId)
        => _runRepo.GetByIdAsync(runId);

    public Task<IReadOnlyList<PayrollRun>> GetRunsByContextAsync(Guid payrollContextId)
        => _runRepo.GetByContextAsync(payrollContextId);

    private async Task<PayrollRun> RequireRunAsync(Guid runId)
        => await _runRepo.GetByIdAsync(runId)
           ?? throw new InvalidOperationException($"Payroll run {runId} not found.");

    private void RequireStatus(PayrollRun run, string requiredCode, string action)
    {
        var requiredId = StatusId(requiredCode);
        if (run.RunStatusId != requiredId)
        {
            var currentCode = _lookup.GetCode(LookupTables.RunStatus, run.RunStatusId);
            throw new InvalidOperationException(
                $"Run {run.RunId} must be in {requiredCode} state to {action}. Current: {currentCode}");
        }
    }
}
