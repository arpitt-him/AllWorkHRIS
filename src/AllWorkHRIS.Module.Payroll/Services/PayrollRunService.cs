using System.Text.Json;
using System.Threading.Channels;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Payroll.Commands;
using AllWorkHRIS.Module.Payroll.Domain.Run;
using AllWorkHRIS.Module.Payroll.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class PayrollRunService : IPayrollRunService
{
    // IAccumulatorService was a dependency of the old cancel-from-Calculated reversal path.
    // ADR-017 (Phase 12.5.3) removed that path — cancel-from-Calculated is now a clean discard
    // since calculation no longer posts to the ledger. Re-add it if/when an explicit "reverse an
    // approved run" surface is built (separate from cancel). IEmployeePayrollResultRepository was
    // re-added (ToDo #43) for the scoped-run already-paid guard below.
    private readonly IPayrollRunRepository             _runRepo;
    private readonly IPayrollContextRepository         _contextRepo;
    private readonly IEmployeePayrollResultRepository  _resultRepo;
    private readonly Channel<Guid>                     _queue;
    private readonly ITemporalContext                  _temporal;
    private readonly ILogger<PayrollRunService>        _logger;
    private readonly IAuditService                     _auditService;
    private readonly ILookupCache                      _lookup;
    private readonly IPayrollHoursSource               _hoursSource;
    private readonly IPayrollCorrectionService         _correctionService;

    public PayrollRunService(
        IPayrollRunRepository             runRepo,
        IPayrollContextRepository         contextRepo,
        IEmployeePayrollResultRepository  resultRepo,
        Channel<Guid>                     queue,
        ITemporalContext                  temporal,
        ILogger<PayrollRunService>        logger,
        IAuditService                     auditService,
        ILookupCache                      lookup,
        IPayrollHoursSource               hoursSource,
        IPayrollCorrectionService         correctionService)
    {
        _runRepo           = runRepo;
        _contextRepo       = contextRepo;
        _resultRepo        = resultRepo;
        _queue             = queue;
        _temporal          = temporal;
        _logger            = logger;
        _auditService      = auditService;
        _lookup            = lookup;
        _hoursSource       = hoursSource;
        _correctionService = correctionService;
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

        // Phase 12.6 — a scoped (targeted) run: create the run_scope defining its population, then
        // link the run to it. Only non-Regular runs may be scoped; a trigger reason is required;
        // the scope is parented to the period's Regular run (the run it supplements).
        Guid? runScopeId  = null;
        Guid? parentRunId = command.ParentRunId;
        if (command.TargetEmploymentIds is { Count: > 0 } targets)
        {
            if (command.RunTypeId == _lookup.GetId(LookupTables.RunType, "REGULAR"))
                throw new InvalidOperationException(
                    "A Regular run pays the full payroll context and cannot be scoped to selected employees. " +
                    "Use a Supplemental, Adjustment, or Correction run to target a subset.");
            if (string.IsNullOrWhiteSpace(command.TriggerReason))
                throw new InvalidOperationException("A scoped run requires a trigger reason.");

            var parent = await _runRepo.GetActiveRegularRunForPeriodAsync(command.PeriodId)
                ?? throw new InvalidOperationException(
                    "A scoped run requires an existing Regular run for the period to supplement.");
            parentRunId ??= parent.RunId;

            var distinctTargets = targets.Distinct().ToList();

            // ToDo #43 — block creating a scoped run that targets an already-paid employee. A
            // scoped run is additive (re-target + re-post, no correction logic), so paying an
            // employee who already has a standing posted result for this period would double-count.
            // The genuine re-pay path is a correction (reverse + re-post), which isn't built yet.
            // The engine's population resolver enforces the same invariant as a safety net (and
            // catches the create→approve race); this is the early, user-facing guard.
            var alreadyPaid = (await _resultRepo.GetPaidEmploymentIdsForPeriodAsync(command.PeriodId, PaidStatusIds()))
                .ToHashSet();
            var paidTargets = distinctTargets.Where(alreadyPaid.Contains).ToList();
            if (paidTargets.Count > 0)
                throw new InvalidOperationException(
                    $"{paidTargets.Count} of the selected employee(s) already have approved pay for this period " +
                    "and would be paid again. A scoped run is additive, not a correction — deselect them to proceed " +
                    "(re-paying requires a correction that reverses and re-posts, which isn't available yet).");
            var scope = new RunScope
            {
                RunScopeId           = Guid.NewGuid(),
                ParentRunId          = parent.RunId,
                PayrollContextId     = command.PayrollContextId,
                ScopeTypeId          = _lookup.GetId(LookupTables.ScopeType, "CATCH_UP"),
                ScopeStatusId        = _lookup.GetId(LookupTables.ScopeStatus, "READY"),
                TriggerReason        = command.TriggerReason!.Trim(),
                PopulationMethodId   = _lookup.GetId(LookupTables.PopulationMethod,
                                          command.ExceptionDerived ? "EXCEPTION" : "EXPLICIT"),
                PopulationDefinition = JsonSerializer.Serialize(distinctTargets.Select(t => t.ToString()).ToList()),
                PopulationCount      = distinctTargets.Count,
                ExceptionDerivedFlag = command.ExceptionDerived,
                PriorityLevel        = "CATCH_UP",
                AdjustmentFlag       = false,
                CreatedBy            = command.InitiatedBy,
                CreationTimestamp    = now
            };
            await _runRepo.InsertRunScopeAsync(scope);
            runScopeId = scope.RunScopeId;
        }

        var run = new PayrollRun
        {
            RunId                      = Guid.NewGuid(),
            PayrollContextId           = command.PayrollContextId,
            PeriodId                   = command.PeriodId,
            PayDate                    = period.PayDate,
            RunTypeId                  = command.RunTypeId,
            RunStatusId                = StatusId("DRAFT"),
            RunDescription             = command.RunDescription,
            ParentRunId                = parentRunId,
            RelatedRunGroupId          = null,
            RunScopeId                 = runScopeId,
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

        // ADR-027 D8 (ordered-commit): approval is the YTD-commit point, so a run cannot
        // commit while an EARLIER-pay-date run in the same context is still un-approved —
        // that would post YTD out of pay-date order. CANCELLED/REVERSED/FAILED don't block.
        var earlierUnapproved = await FindEarlierBlockingRunAsync(run, BlocksApproval);
        if (earlierUnapproved is not null)
            throw new InvalidOperationException(OrderedCommitMessage(run, earlierUnapproved, "approve"));

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

        // ADR-027 D8 (ordered-commit): runs finalize (release) in pay-date order, so a run
        // cannot release while an EARLIER-pay-date run in the same context is not yet RELEASED.
        // Release-order is its own gate, not merely a corollary of approval-order: two runs can
        // both be APPROVED, and nothing else stops the later-dated one releasing first.
        // CANCELLED/REVERSED/FAILED don't block.
        var earlierUnreleased = await FindEarlierBlockingRunAsync(run, BlocksRelease);
        if (earlierUnreleased is not null)
            throw new InvalidOperationException(OrderedCommitMessage(run, earlierUnreleased, "release"));

        await _runRepo.UpdateStatusAsync(command.RunId, StatusId("RELEASING"), command.ReleasedBy);
        _queue.Writer.TryWrite(command.RunId);
        _logger.LogInformation("Run {RunId} release initiated by {UserId}", command.RunId, command.ReleasedBy);
    }

    public async Task ResumeRunAsync(ResumePayrollRunCommand command)
    {
        // ADR-017 / Phase 12.5.x: in-UI counterpart to PayrollRunJob's
        // host-startup recovery. Only the idempotent transient states are
        // resumable — APPROVING (per-result post is skip-if-already-posted)
        // and RELEASING (a plain status flip). CALCULATING is deliberately
        // excluded: the calc pass is not idempotent and re-running would
        // orphan a partial result set (same reasoning that makes recovery
        // FAIL an interrupted calc rather than resume it).
        //
        // No status change here — the run is already in the transient state;
        // we just re-enqueue. The queue has a single consumer, so a re-enqueue
        // is serialized behind any genuinely-running job and the job's own
        // idempotency guards make the second pass a no-op.
        var run = await RequireRunAsync(command.RunId);

        var approvingId = StatusId("APPROVING");
        var releasingId = StatusId("RELEASING");
        if (run.RunStatusId != approvingId && run.RunStatusId != releasingId)
        {
            var currentCode = _lookup.GetCode(LookupTables.RunStatus, run.RunStatusId);
            throw new InvalidOperationException(
                $"Run {command.RunId} can only be resumed from APPROVING or RELEASING. Current: {currentCode}");
        }

        _queue.Writer.TryWrite(command.RunId);

        var statusCode = _lookup.GetCode(LookupTables.RunStatus, run.RunStatusId);
        _logger.LogInformation("Run {RunId} resumed (re-enqueued from {Status}) by {UserId}",
            command.RunId, statusCode, command.ResumedBy);

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:       "STATUS_CHANGE",
            EntityType:      "PayrollRun",
            EntityId:        command.RunId,
            ModuleName:      "PAYROLL",
            ChangeSummary:   $"Payroll run manually resumed (re-enqueued from {statusCode})",
            ParentEntityType: "PayrollContext",
            ParentEntityId:  run.PayrollContextId,
            AfterJson:       JsonSerializer.Serialize(new { run_status = statusCode, resumed = true })
        ));
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

        // Phase 12.7 — unlock-on-cancel: release any time entries this run had locked, returning
        // them to the pool for a fresh run. Cancel today is only from Draft/Calculated (no locks
        // yet, since locking happens at approval), so this is a no-op now and the safety net for
        // when a locked (approved) run can be reversed/cancelled — closes the stranded-lock gap.
        await _hoursSource.UnlockHoursForRunAsync(command.RunId);

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

    // ADR-027: UI/command path for reverse-an-approved-run — a thin delegator to the standalone,
    // reusable correction service (the same primitive the future re-pay / true-up / EWA paths call).
    public Task<ReversalOutcome> ReverseRunAsync(ReversePayrollRunCommand command)
        => _correctionService.ReverseRunAsync(new ReverseRunRequest
        {
            RunId      = command.RunId,
            ReversedBy = command.ReversedBy,
            Reason     = command.Reason
        });

    public Task<ReversalOutcome> ReverseEmploymentResultsAsync(ReverseEmploymentResultsCommand command)
        => _correctionService.ReverseResultsAsync(new ReverseResultsRequest
        {
            RunId         = command.RunId,
            EmploymentIds = command.EmploymentIds,
            ReversedBy    = command.ReversedBy,
            Reason        = command.Reason
        });

    public Task<PayrollRun?> GetRunByIdAsync(Guid runId)
        => _runRepo.GetByIdAsync(runId);

    public Task<IReadOnlyList<PayrollRun>> GetRunsByContextAsync(Guid payrollContextId)
        => _runRepo.GetByContextAsync(payrollContextId);

    // ── Phase 12.6 — scoped/targeted run support ────────────────────────────
    public Task<IReadOnlyList<RunTargetEmployee>> GetTargetableEmployeesAsync(Guid payrollContextId)
        => _runRepo.GetTargetableEmployeesByContextAsync(payrollContextId);

    public async Task<IReadOnlyList<Guid>> GetCarryoverEmploymentIdsAsync(Guid periodId)
    {
        // The catch-up case: pre-fill the picker from the period's Regular run exceptions
        // (BLOCKING_TASKS_INCOMPLETE, NET_PAY_FLOOR_APPLIED, etc.). No Regular run yet → empty.
        var parent = await _runRepo.GetActiveRegularRunForPeriodAsync(periodId);
        if (parent is null) return [];
        var exceptions = await _runRepo.GetRunExceptionsAsync(parent.RunId);
        return exceptions.Select(e => e.EmploymentId).Distinct().ToList();
    }

    // ToDo #43 — employees already paid (standing posted result) for the period. The New Run
    // picker uses this to badge / block already-paid targets at selection time; the creation guard
    // below and the engine resolver enforce the same invariant as safety nets.
    public async Task<IReadOnlyList<Guid>> GetAlreadyPaidEmploymentIdsAsync(Guid periodId)
        => await _resultRepo.GetPaidEmploymentIdsForPeriodAsync(periodId, PaidStatusIds());

    // The employee-result statuses that represent a *standing posted* payment. REVERSED /
    // CORRECTED are deliberately absent — those are what a correction produces, not a payment.
    private int[] PaidStatusIds() =>
        [
            _lookup.GetId(LookupTables.EmployeeResultStatus, "APPROVED"),
            _lookup.GetId(LookupTables.EmployeeResultStatus, "RELEASED"),
            _lookup.GetId(LookupTables.EmployeeResultStatus, "FINALIZED"),
        ];

    public Task<RunScope?> GetRunScopeAsync(Guid runScopeId)
        => _runRepo.GetRunScopeAsync(runScopeId);

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

    // -----------------------------------------------------------------------
    // ADR-027 D8 — ordered-commit gate (finalize runs in pay-date order)
    // -----------------------------------------------------------------------

    // Per-context run order: pay date asc → Regular before a same-date Supplemental/correction
    // → creation time → run id (deterministic total order). The same key drives both gates.
    private (DateOnly Pay, int TypeRank, DateTimeOffset Created, Guid Id) OrderKey(PayrollRun r)
        => (r.PayDate, TypeRank(r), r.CreationTimestamp, r.RunId);

    // Regular sorts ahead of any other run type sharing a pay date (a Supplemental/correction is
    // typically a re-pay or top-up of that pay date's Regular, so the Regular finalizes first).
    private int TypeRank(PayrollRun r)
        => r.RunTypeId == _lookup.GetId(LookupTables.RunType, "REGULAR") ? 0 : 1;

    private string RunTypeLabel(int runTypeId)
        => _lookup.GetAll(LookupTables.RunType).FirstOrDefault(e => e.Id == runTypeId)?.Label
           ?? _lookup.GetCode(LookupTables.RunType, runTypeId);

    private static int CompareKey(
        (DateOnly Pay, int TypeRank, DateTimeOffset Created, Guid Id) a,
        (DateOnly Pay, int TypeRank, DateTimeOffset Created, Guid Id) b)
    {
        int c = a.Pay.CompareTo(b.Pay);          if (c != 0) return c;
        c = a.TypeRank.CompareTo(b.TypeRank);    if (c != 0) return c;
        c = a.Created.CompareTo(b.Created);      if (c != 0) return c;
        return a.Id.CompareTo(b.Id);
    }

    // An earlier run still pending its YTD commit (un-approved) blocks a later run's APPROVAL.
    private static bool BlocksApproval(string statusCode)
        => statusCode is "DRAFT" or "OPEN" or "CALCULATING" or "CALCULATED" or "UNDER_REVIEW" or "APPROVING";

    // An earlier run not yet finalized (released) and not void blocks a later run's RELEASE.
    // RELEASED is done; CANCELLED/REVERSED/FAILED are void — none of these block.
    private static bool BlocksRelease(string statusCode)
        => statusCode is "DRAFT" or "OPEN" or "CALCULATING" or "CALCULATED" or "UNDER_REVIEW"
                      or "APPROVING" or "APPROVED" or "RELEASING";

    // Returns the EARLIEST strictly-earlier same-context run whose status satisfies `blocks`,
    // or null if `run` is free to proceed. Earliest blocker → clearest "do X first" message.
    private async Task<PayrollRun?> FindEarlierBlockingRunAsync(PayrollRun run, Func<string, bool> blocks)
    {
        var siblings = await _runRepo.GetByContextAsync(run.PayrollContextId);
        var runKey   = OrderKey(run);

        PayrollRun? blocker = null;
        foreach (var s in siblings)
        {
            if (s.RunId == run.RunId) continue;
            if (CompareKey(OrderKey(s), runKey) >= 0) continue;   // strictly-earlier runs only
            if (!blocks(_lookup.GetCode(LookupTables.RunStatus, s.RunStatusId))) continue;
            if (blocker is null || CompareKey(OrderKey(s), OrderKey(blocker)) < 0)
                blocker = s;
        }
        return blocker;
    }

    // Builds the blocked message, naming the criterion that actually ordered `blocker` ahead of
    // `current` (earlier pay date, OR same pay date with the run-type or creation-order tiebreak) —
    // important because when both share a pay date, "finalize in pay-date order" alone reads as a
    // contradiction. `verb` is "approve" | "release" (past form derived as verb + "d").
    private string OrderedCommitMessage(PayrollRun current, PayrollRun blocker, string verb)
    {
        var name = string.IsNullOrWhiteSpace(blocker.RunDescription) ? blocker.RunId.ToString() : blocker.RunDescription;
        var done = verb + "d";   // approve → approved, release → released

        string why;
        if (blocker.PayDate < current.PayDate)
            why = $"it has an earlier pay date ({blocker.PayDate:yyyy-MM-dd})";
        else if (TypeRank(blocker) < TypeRank(current))
            why = $"on the same pay date ({blocker.PayDate:yyyy-MM-dd}) a {RunTypeLabel(blocker.RunTypeId)} run " +
                  $"finalizes before this {RunTypeLabel(current.RunTypeId)} run";
        else
            why = $"on the same pay date ({blocker.PayDate:yyyy-MM-dd}) it was created earlier";

        return $"Can't {verb} this run yet: {name} must be {done} first because {why}. " +
               $"Runs in a context finalize in a fixed order — pay date, then run type " +
               $"(Regular before a same-date Supplemental), then creation order.";
    }

    // UI pre-check: a message if this run is blocked from its next finalize step by an earlier
    // run (approval when CALCULATED, release when APPROVED), else null. Lets the run-detail page
    // disable the Approve/Release action and show why, rather than only throwing on click.
    public async Task<string?> GetOrderedCommitBlockAsync(Guid runId)
    {
        var run = await RequireRunAsync(runId);
        if (run.RunStatusId == StatusId("CALCULATED"))
        {
            var b = await FindEarlierBlockingRunAsync(run, BlocksApproval);
            return b is null ? null : OrderedCommitMessage(run, b, "approve");
        }
        if (run.RunStatusId == StatusId("APPROVED"))
        {
            var b = await FindEarlierBlockingRunAsync(run, BlocksRelease);
            return b is null ? null : OrderedCommitMessage(run, b, "release");
        }
        return null;
    }
}
