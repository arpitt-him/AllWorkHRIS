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

    // Reverse the whole run = reverse every employee's result (the "all" case of ReverseResultsAsync).
    public async Task<ReversalOutcome> ReverseRunAsync(ReverseRunRequest request, CancellationToken ct = default)
        => await ReverseCoreAsync(request.RunId, employmentIds: null, request.ReversedBy, request.Reason, ct);

    // Reverse only the selected employees' results (per-EE granularity).
    public async Task<ReversalOutcome> ReverseResultsAsync(ReverseResultsRequest request, CancellationToken ct = default)
        => await ReverseCoreAsync(request.RunId, request.EmploymentIds, request.ReversedBy, request.Reason, ct);

    // <paramref name="employmentIds"/> null ⇒ reverse all standing results (run-level reverse).
    private async Task<ReversalOutcome> ReverseCoreAsync(
        Guid runId, IReadOnlyCollection<Guid>? employmentIds, Guid reversedBy, string reason, CancellationToken ct)
    {
        var run = await _runRepo.GetByIdAsync(runId)
            ?? throw new InvalidOperationException($"Payroll run {runId} not found.");

        var approvedRunId = _lookup.GetId(LookupTables.RunStatus, "APPROVED");
        var reversedRunId = _lookup.GetId(LookupTables.RunStatus, "REVERSED");

        // Idempotent no-op: the whole run is already reversed. (ReverseAsync is NOT safe to run twice
        // on a result — it would negate the negating rows — so this is the safety, not a nicety.)
        if (run.RunStatusId == reversedRunId)
        {
            _logger.LogInformation("Run {RunId} is already REVERSED; reverse is a no-op.", runId);
            return new ReversalOutcome { RunId = runId, AlreadyReversed = true, ReversedResultIds = [] };
        }

        // Increment 1 scope: only an APPROVED (impacts posted, not yet released) run can be reversed.
        // A RELEASED run means money is out the door — that is reverse + re-pay / W-2c (ADR-027 D5).
        if (run.RunStatusId != approvedRunId)
        {
            var code = _lookup.GetCode(LookupTables.RunStatus, run.RunStatusId);
            throw new InvalidOperationException(
                $"Run {runId} cannot be reversed from status {code}. Only an APPROVED run can be reversed " +
                $"(reversing a RELEASED run requires the re-pay / correction flow, which is not yet available).");
        }

        var approvedResultId = _lookup.GetId(LookupTables.EmployeeResultStatus, "APPROVED");
        var reversedResultId = _lookup.GetId(LookupTables.EmployeeResultStatus, "REVERSED");

        var results  = await _resultRepo.GetByRunIdAsync(runId);
        var targetSet = employmentIds?.ToHashSet();   // null ⇒ all
        var reversedIds = new List<Guid>();

        // Contra-post each *selected* standing (APPROVED) result. ReverseAsync inserts negating impact
        // rows and reverts balances to their pre-run value (forward-only — no deletes). Each call is
        // atomic; skipping non-APPROVED results makes a re-invocation after a partial failure safe.
        foreach (var result in results)
        {
            if (result.ResultStatusId != approvedResultId) continue;            // leave FAILED/REVERSED/other
            if (targetSet is not null && !targetSet.Contains(result.EmploymentId)) continue;   // not selected

            await _accumulator.ReverseAsync(result.EmployeePayrollResultId, reversedBy, ct);
            await _resultRepo.UpdateStatusAsync(result.EmployeePayrollResultId, reversedResultId);
            reversedIds.Add(result.EmployeePayrollResultId);
            ct.ThrowIfCancellationRequested();
        }

        // The run becomes REVERSED only when no standing (APPROVED) result remains — i.e. a full
        // reverse (or a partial that happened to cover everyone). Then the period reopens and the
        // run's locks release. A partial reversal leaves the run APPROVED (still covers the rest).
        var standingRemaining = results.Count(r =>
            r.ResultStatusId == approvedResultId &&
            (targetSet is null ? false : !targetSet.Contains(r.EmploymentId)));
        var fullyReversed = reversedIds.Count > 0 && standingRemaining == 0;

        if (fullyReversed)
        {
            await _runRepo.UpdateStatusAsync(runId, reversedRunId, reversedBy);
            await _hoursSource.UnlockHoursForRunAsync(runId, ct);
        }

        _logger.LogInformation(
            "Run {RunId}: {Count} result(s) reversed by {UserId} (run {RunState}): {Reason}",
            runId, reversedIds.Count, reversedBy, fullyReversed ? "REVERSED" : "still APPROVED", reason);

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:        "STATUS_CHANGE",
            EntityType:       "PayrollRun",
            EntityId:         runId,
            ModuleName:       "PAYROLL",
            ChangeSummary:    fullyReversed
                ? $"Payroll run reversed ({reversedIds.Count} results contra-posted): {reason}"
                : $"Payroll run partially reversed ({reversedIds.Count} result(s) contra-posted, run remains approved): {reason}",
            ParentEntityType: "PayrollContext",
            ParentEntityId:   run.PayrollContextId,
            AfterJson:        JsonSerializer.Serialize(new
            {
                run_status       = fullyReversed ? "REVERSED" : "APPROVED",
                results_reversed = reversedIds.Count,
                reason
            })
        ));

        return new ReversalOutcome { RunId = runId, AlreadyReversed = false, ReversedResultIds = reversedIds };
    }
}
