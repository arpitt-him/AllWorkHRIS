using System.Text.Json;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Payroll.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll.Services;

/// <summary>
/// ADR-027 implementation. A standalone, callable correction/re-pay service — see
/// <see cref="IPayrollCorrectionService"/> for the design intent (one primitive, five consumers).
/// </summary>
public sealed class PayrollCorrectionService : IPayrollCorrectionService
{
    private readonly IPayrollRunRepository            _runRepo;
    private readonly IEmployeePayrollResultRepository _resultRepo;
    private readonly IAccumulatorService              _accumulator;
    private readonly IPayrollHoursSource              _hoursSource;
    private readonly ILookupCache                     _lookup;
    private readonly IAuditService                    _auditService;
    private readonly ILogger<PayrollCorrectionService> _logger;

    public PayrollCorrectionService(
        IPayrollRunRepository            runRepo,
        IEmployeePayrollResultRepository resultRepo,
        IAccumulatorService             accumulator,
        IPayrollHoursSource             hoursSource,
        ILookupCache                    lookup,
        IAuditService                   auditService,
        ILogger<PayrollCorrectionService> logger)
    {
        _runRepo      = runRepo;
        _resultRepo   = resultRepo;
        _accumulator  = accumulator;
        _hoursSource  = hoursSource;
        _lookup       = lookup;
        _auditService = auditService;
        _logger       = logger;
    }

    public async Task<ReversalOutcome> ReverseRunAsync(ReverseRunRequest request, CancellationToken ct = default)
    {
        var run = await _runRepo.GetByIdAsync(request.RunId)
            ?? throw new InvalidOperationException($"Payroll run {request.RunId} not found.");

        var approvedRunId = _lookup.GetId(LookupTables.RunStatus, "APPROVED");
        var reversedRunId = _lookup.GetId(LookupTables.RunStatus, "REVERSED");

        // Idempotent no-op: already reversed. (ReverseAsync is NOT safe to run twice on a result —
        // it would negate the negating rows — so this run-level guard is the safety, not a nicety.)
        if (run.RunStatusId == reversedRunId)
        {
            _logger.LogInformation("Run {RunId} is already REVERSED; reverse is a no-op.", request.RunId);
            return new ReversalOutcome { RunId = request.RunId, AlreadyReversed = true, ReversedResultIds = [] };
        }

        // Increment 1 scope: only an APPROVED (impacts posted, not yet released) run can be reversed.
        // A RELEASED run means money is out the door — that is reverse + re-pay / W-2c (ADR-027 D5),
        // a later increment.
        if (run.RunStatusId != approvedRunId)
        {
            var code = _lookup.GetCode(LookupTables.RunStatus, run.RunStatusId);
            throw new InvalidOperationException(
                $"Run {request.RunId} cannot be reversed from status {code}. Only an APPROVED run can be reversed " +
                $"(reversing a RELEASED run requires the re-pay / correction flow, which is not yet available).");
        }

        var approvedResultId = _lookup.GetId(LookupTables.EmployeeResultStatus, "APPROVED");
        var reversedResultId = _lookup.GetId(LookupTables.EmployeeResultStatus, "REVERSED");

        var results = await _resultRepo.GetByRunIdAsync(request.RunId);
        var reversedIds = new List<Guid>();

        // Contra-post each standing (APPROVED) result. ReverseAsync inserts negating impact rows and
        // reverts balances to their pre-run value (forward-only — no deletes). Each call is atomic;
        // skipping already-REVERSED results makes a re-invocation after a partial failure safe.
        foreach (var result in results)
        {
            if (result.ResultStatusId != approvedResultId) continue;   // leave FAILED/other as-is

            await _accumulator.ReverseAsync(result.EmployeePayrollResultId, request.ReversedBy, ct);
            await _resultRepo.UpdateStatusAsync(result.EmployeePayrollResultId, reversedResultId);
            reversedIds.Add(result.EmployeePayrollResultId);
            ct.ThrowIfCancellationRequested();
        }

        // Mark the run REVERSED (excluded from the one-Regular-run-per-period guard, so the period
        // reopens) and release the time-entry locks back to the pool for a fresh run.
        await _runRepo.UpdateStatusAsync(request.RunId, reversedRunId, request.ReversedBy);
        await _hoursSource.UnlockHoursForRunAsync(request.RunId, ct);

        _logger.LogInformation(
            "Run {RunId} reversed by {UserId} ({Count} results contra-posted): {Reason}",
            request.RunId, request.ReversedBy, reversedIds.Count, request.Reason);

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:        "STATUS_CHANGE",
            EntityType:       "PayrollRun",
            EntityId:         request.RunId,
            ModuleName:       "PAYROLL",
            ChangeSummary:    $"Payroll run reversed ({reversedIds.Count} results contra-posted): {request.Reason}",
            ParentEntityType: "PayrollContext",
            ParentEntityId:   run.PayrollContextId,
            AfterJson:        JsonSerializer.Serialize(new
            {
                run_status      = "REVERSED",
                results_reversed = reversedIds.Count,
                reason          = request.Reason
            })
        ));

        return new ReversalOutcome
        {
            RunId             = request.RunId,
            AlreadyReversed   = false,
            ReversedResultIds = reversedIds
        };
    }
}
