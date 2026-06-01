using System.Text.Json;
using System.Threading.Channels;
using Autofac;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Payroll.Domain.Results;
using AllWorkHRIS.Module.Payroll.Domain.ResultSet;
using AllWorkHRIS.Module.Payroll.Domain.Run;
using AllWorkHRIS.Module.Payroll.Repositories;
using AllWorkHRIS.Module.Payroll.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll.Jobs;

/// <summary>
/// Long-running background service that dequeues payroll run IDs and drives
/// each through the relevant lifecycle phase (calculation, approval, release).
/// A child lifetime scope is created per run so scoped services
/// (repositories, engine) resolve correctly from a singleton.
///
/// Status ids are resolved through ILookupCache against the seeded code
/// strings — never hard-coded as integer literals. The seed in
/// payroll_lookups_seed_data.sql is the single source of truth; the
/// auto-incremented ids are an internal detail.
/// </summary>
public sealed class PayrollRunJob : BackgroundService
{
    private readonly Channel<Guid>          _queue;
    private readonly ILifetimeScope         _rootScope;
    private readonly ILogger<PayrollRunJob> _logger;
    private readonly IRunProgressNotifier   _progress;

    public PayrollRunJob(
        Channel<Guid>          queue,
        ILifetimeScope         rootScope,
        ILogger<PayrollRunJob> logger,
        IRunProgressNotifier   progress)
    {
        _queue     = queue;
        _rootScope = rootScope;
        _logger    = logger;
        _progress  = progress;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ADR-017 / Phase 12.5.3: recover runs orphaned in a transient state by a
        // host restart (their in-memory queue entry was lost) before resuming
        // normal queue processing — closes the "Releasing/Approving limbo" class.
        await RecoverOrphanedRunsAsync(stoppingToken);

        await foreach (var runId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessRunAsync(runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing payroll run {RunId}", runId);
            }
        }
    }

    /// <summary>
    /// Host-startup recovery (ADR-017 / Phase 12.5.3). A hard restart loses the
    /// in-memory queue, leaving runs stuck in a transient state:
    ///   • APPROVING / RELEASING are idempotent (ProcessApproveAsync skips
    ///     already-posted results; release is a plain status flip), so we
    ///     re-enqueue them and they finish on this boot.
    ///   • CALCULATING is NOT idempotent — the calc loop creates a fresh result
    ///     set each pass and there is no physical result-discard (cancel is a
    ///     logical status flip). Re-running would orphan a partial result set and
    ///     risk double-posting at approval, so we fail it instead. FAILED does not
    ///     block a new Regular run for the period, so the operator re-initiates.
    /// Wrapped so a recovery failure never stops normal queue processing.
    /// </summary>
    private async Task RecoverOrphanedRunsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _rootScope.BeginLifetimeScope();
            var runRepo = scope.Resolve<IPayrollRunRepository>();
            var lookup  = scope.Resolve<ILookupCache>();

            var orphans = await runRepo.GetRunsInTransientStatesAsync();
            if (orphans.Count == 0) return;

            var approvingStatus = lookup.GetId(LookupTables.RunStatus, "APPROVING");
            var releasingStatus = lookup.GetId(LookupTables.RunStatus, "RELEASING");
            var failedStatus    = lookup.GetId(LookupTables.RunStatus, "FAILED");

            int requeued = 0, failedCalc = 0;
            foreach (var run in orphans)
            {
                ct.ThrowIfCancellationRequested();

                if (run.RunStatusId == approvingStatus || run.RunStatusId == releasingStatus)
                {
                    _queue.Writer.TryWrite(run.RunId);
                    requeued++;
                    _logger.LogInformation(
                        "Recovery: re-enqueued {Status} run {RunId} after host restart.",
                        lookup.GetCode(LookupTables.RunStatus, run.RunStatusId), run.RunId);
                }
                else // CALCULATING — see method remarks; cannot be safely resumed.
                {
                    await runRepo.UpdateStatusAsync(run.RunId, failedStatus, run.InitiatedBy);
                    failedCalc++;
                    _logger.LogWarning(
                        "Recovery: run {RunId} was mid-CALCULATING at restart and cannot be safely "
                        + "resumed; marked FAILED — re-initiate the run for this period.", run.RunId);
                }
            }

            _logger.LogInformation(
                "Host-startup payroll recovery: {Requeued} run(s) re-enqueued, {FailedCalc} interrupted calc run(s) failed.",
                requeued, failedCalc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Host-startup payroll run recovery failed; continuing to normal queue processing.");
        }
    }

    private async Task ProcessRunAsync(Guid runId, CancellationToken ct)
    {
        await using var scope = _rootScope.BeginLifetimeScope();

        var runRepo       = scope.Resolve<IPayrollRunRepository>();
        var resultSetRepo = scope.Resolve<IPayrollRunResultSetRepository>();
        var resultRepo    = scope.Resolve<IEmployeePayrollResultRepository>();
        var profileRepo   = scope.Resolve<IPayrollProfileRepository>();
        var contextRepo   = scope.Resolve<IPayrollContextRepository>();
        var compSnapshot  = scope.Resolve<IPayrollCompensationSnapshotRepository>();
        var engine        = scope.Resolve<ICalculationEngine>();
        var temporal      = scope.Resolve<ITemporalContext>();
        var wallClock     = scope.Resolve<IWallClock>();
        var lookup        = scope.Resolve<ILookupCache>();
        // IAccumulatorService and IAccumulatorRepository are resolved by
        // ProcessApproveAsync — the calc loop no longer touches the ledger
        // (ADR-017, Phase 12.5.3).

        // Audit-style timestamp: TDO operative date + real time-of-day (so a run's
        // duration is visible), stored at UTC offset. Centralised in GetOperativeNow().
        DateTimeOffset TdoNow() => temporal.GetOperativeNow();

        // Resolve status ids once per run rather than on every iteration.
        int RunStatusId(string code) => lookup.GetId(LookupTables.RunStatus, code);
        int ResultStatusId(string code) => lookup.GetId(LookupTables.EmployeeResultStatus, code);

        var releasingStatus   = RunStatusId("RELEASING");
        var releasedStatus    = RunStatusId("RELEASED");
        var approvingStatus   = RunStatusId("APPROVING");
        var calculatingStatus = RunStatusId("CALCULATING");
        var calculatedStatus  = RunStatusId("CALCULATED");
        var failedStatus      = RunStatusId("FAILED");
        var cancelledStatus   = RunStatusId("CANCELLED");

        var inProgressResultStatus = ResultStatusId("IN_PROGRESS");
        var calculatedResultStatus = ResultStatusId("CALCULATED");
        var failedResultStatus     = ResultStatusId("FAILED");

        var run = await runRepo.GetByIdAsync(runId);
        if (run is null)
        {
            _logger.LogWarning("Run {RunId} not found — skipping", runId);
            return;
        }

        // Release path — no calculation needed, just finalise
        if (run.RunStatusId == releasingStatus)
        {
            await runRepo.UpdateStatusAsync(runId, releasedStatus, run.InitiatedBy);
            await contextRepo.UpdatePeriodStatusAsync(run.PeriodId, "CLOSED", run.InitiatedBy);
            _logger.LogInformation("Run {RunId} released; period {PeriodId} closed", runId, run.PeriodId);
            return;
        }

        // Approval path — post accumulator impacts for each succeeded result,
        // then transition to Approved. ADR-017 (Phase 12.5.3): approval is the
        // YTD-commit point; calculation produces no ledger writes.
        if (run.RunStatusId == approvingStatus)
        {
            await ProcessApproveAsync(scope, run, lookup, ct);
            return;
        }

        // Transition to CALCULATING
        var startTime = TdoNow();
        await runRepo.UpdateStatusAsync(runId, calculatingStatus, run.InitiatedBy);
        await runRepo.SetRunTimestampsAsync(runId, startTime, null, run.InitiatedBy);

        try
        {
            // Create result set for this calculation pass
            var now = TdoNow();
            var resultSet = new PayrollRunResultSet
            {
                PayrollRunResultSetId        = Guid.NewGuid(),
                PayrollRunId                 = runId,
                RunScopeId                   = run.RunScopeId,
                SourcePeriodId               = run.PeriodId,
                ExecutionPeriodId            = run.PeriodId,
                ParentPayrollRunResultSetId  = null,
                RootPayrollRunResultSetId    = null,
                ResultSetLineageSequence     = 1,
                CorrectionReferenceId        = null,
                ResultSetStatusId            = lookup.GetId(LookupTables.ResultSetStatus, "PENDING"),
                ResultSetTypeId              = lookup.GetId(LookupTables.ResultSetType, "REGULAR_RUN"),
                ExecutionStartTimestamp      = now,
                ExecutionEndTimestamp        = null,
                ApprovalRequiredFlag         = false,
                ApprovedByUserId             = null,
                ApprovalTimestamp            = null,
                FinalizationTimestamp        = null,
                CreatedTimestamp             = now,
                UpdatedTimestamp             = now
            };
            await resultSetRepo.InsertAsync(resultSet);

            // Resolve pay frequency, OT threshold, and period dates (same for all employees in the run)
            var periodsPerYear = await contextRepo.GetPeriodsPerYearAsync(run.PayrollContextId);
            var payrollContext  = await contextRepo.GetByIdAsync(run.PayrollContextId)
                                  ?? throw new InvalidOperationException(
                                      $"Payroll context {run.PayrollContextId} not found for run {runId}");
            var period         = await contextRepo.GetPeriodByIdAsync(run.PeriodId)
                                 ?? throw new InvalidOperationException($"Period {run.PeriodId} not found for run {runId}");

            // Resolve the employee population + blocked set for this run. A full-context run
            // pays all active+cleared employees and flags every blocked employee; a scoped
            // run (Phase 12.6) narrows both to its validated target list.
            var (population, blocked) = await ResolvePopulationAsync(run, profileRepo, runRepo, lookup);
            var total = population.Count;
            if (blocked.Count > 0)
            {
                var exceptionTime = TdoNow();
                foreach (var blockedId in blocked)
                {
                    await runRepo.InsertRunExceptionAsync(new PayrollRunException
                    {
                        RunExceptionId   = Guid.NewGuid(),
                        RunId            = runId,
                        EmploymentId     = blockedId,
                        ExceptionCode    = "BLOCKING_TASKS_INCOMPLETE",
                        ExceptionMessage = "Employee excluded: onboarding blocking tasks not yet complete.",
                        CreatedTimestamp = exceptionTime
                    });
                    _logger.LogWarning(
                        "Run {RunId}: employment {EmploymentId} excluded — BLOCKING_TASKS_INCOMPLETE",
                        runId, blockedId);
                }
                _logger.LogWarning("Run {RunId}: {Blocked} employee(s) excluded — BLOCKING_TASKS_INCOMPLETE",
                    runId, blocked.Count);
            }

            _logger.LogInformation("Run {RunId}: calculating {Total} employees", runId, total);

            await _progress.UpdateAsync(new RunProgress
            {
                RunId = runId, PercentComplete = 0, Processed = 0, Total = total,
                Failed = 0, StatusMessage = $"Calculating {total} employees…",
                RunStatus = "CALCULATING", UpdatedAt = wallClock.UtcNow
            });

            int processed = 0;
            int failed    = 0;

            foreach (var employmentId in population)
            {
                ct.ThrowIfCancellationRequested();

                var resultId   = Guid.NewGuid();
                var resultTime = TdoNow();

                // Create the employee result header row before writing result lines
                var employeeResult = new EmployeePayrollResult
                {
                    EmployeePayrollResultId          = resultId,
                    PayrollRunResultSetId             = resultSet.PayrollRunResultSetId,
                    PayrollRunId                      = runId,
                    RunScopeId                        = run.RunScopeId,
                    EmploymentId                      = employmentId,
                    PersonId                          = Guid.Empty, // TODO: resolve via PayrollProfile
                    PayrollContextId                  = run.PayrollContextId,
                    SourcePeriodId                    = run.PeriodId,
                    ExecutionPeriodId                 = run.PeriodId,
                    ParentEmployeePayrollResultId     = null,
                    RootEmployeePayrollResultId       = null,
                    ResultLineageSequence             = 1,
                    CorrectionReferenceId             = null,
                    ResultStatusId                    = inProgressResultStatus,
                    PayPeriodStartDate                = period.PeriodStartDate,
                    PayPeriodEndDate                  = period.PeriodEndDate,
                    PayDate                           = run.PayDate,
                    GrossPayAmount                    = 0m,
                    TotalDeductionsAmount             = 0m,
                    TotalEmployeeTaxAmount            = 0m,
                    TotalEmployerContributionAmount   = 0m,
                    NetPayAmount                      = 0m,
                    CreatedTimestamp                  = resultTime,
                    UpdatedTimestamp                  = resultTime
                };
                await resultRepo.InsertAsync(employeeResult);

                var snapshot = await compSnapshot.GetSnapshotAsync(employmentId, run.PayDate);

                var input = new CalculationInput
                {
                    EmployeePayrollResultId = resultId,
                    RunId                   = runId,
                    ResultSetId             = resultSet.PayrollRunResultSetId,
                    EmploymentId            = employmentId,
                    PersonId                = Guid.Empty,
                    PayrollContextId        = run.PayrollContextId,
                    PeriodId                = run.PeriodId,
                    PayDate                 = run.PayDate,
                    AnnualEquivalent        = snapshot?.AnnualEquivalent,
                    BaseRate                = snapshot?.BaseRate ?? 0m,
                    FlsaStatusCode          = snapshot?.FlsaStatusCode,
                    RateTypeCode            = snapshot?.RateTypeCode,
                    OtWeeklyThresholdHours  = payrollContext.OtWeeklyThresholdHours,
                    WorkWeekStartDay        = payrollContext.WorkweekStartDay,
                    PeriodsPerYear          = periodsPerYear,
                    PayPeriodStart          = period.PeriodStartDate,
                    PayPeriodEnd            = period.PeriodEndDate
                };

                var output = await engine.CalculateAsync(input, ct);

                if (output.Succeeded)
                {
                    await resultRepo.UpdateTotalsAsync(
                        resultId,
                        output.GrossPay,
                        output.TotalDeductionsAmount,
                        output.TotalEmployeeTaxAmount,
                        output.TotalEmployerContribAmount,
                        output.NetPay);
                    await resultRepo.UpdateStatusAsync(resultId, calculatedResultStatus);

                    if (output.NetPayFloorApplied)
                    {
                        await runRepo.InsertRunExceptionAsync(new PayrollRunException
                        {
                            RunExceptionId   = Guid.NewGuid(),
                            RunId            = runId,
                            EmploymentId     = employmentId,
                            ExceptionCode    = "NET_PAY_FLOOR_APPLIED",
                            ExceptionMessage = $"Net pay floored to $0.00; {output.NetPayFloorExcess:F4} in deductions could not be collected due to insufficient earnings.",
                            CreatedTimestamp = TdoNow()
                        });
                    }

                    // ADR-017 (Phase 12.5.3): calculation no longer touches the
                    // accumulator ledger. The YTD post happens at approval time
                    // in ProcessApproveAsync below.

                    processed++;
                }
                else
                {
                    await resultRepo.UpdateStatusAsync(resultId, failedResultStatus);
                    _logger.LogWarning(
                        "Run {RunId} employee {EmploymentId} failed: {Reason}",
                        runId, employmentId, output.FailureReason);
                    failed++;
                }

                if (total > 0 && (processed + failed) % 10 == 0)
                {
                    _logger.LogInformation(
                        "Run {RunId}: {Done}/{Total} ({Failed} failed)",
                        runId, processed + failed, total, failed);
                    var pct = (int)((processed + failed) * 100.0 / total);
                    await _progress.UpdateAsync(new RunProgress
                    {
                        RunId = runId, PercentComplete = pct, Processed = processed,
                        Total = total, Failed = failed,
                        StatusMessage = $"Calculated {processed} of {total}…",
                        RunStatus = "CALCULATING", UpdatedAt = wallClock.UtcNow
                    });
                }
            }

            var finalRunStatus = total > 0 && failed == total
                ? failedStatus
                : calculatedStatus;

            await runRepo.UpdateStatusAsync(runId, finalRunStatus, run.InitiatedBy);
            await runRepo.SetRunTimestampsAsync(runId, startTime, TdoNow(), run.InitiatedBy);
            await resultSetRepo.UpdateStatusAsync(resultSet.PayrollRunResultSetId, lookup.GetId(LookupTables.ResultSetStatus, "CALCULATED"));

            var blockedMsg = blocked.Count > 0 ? $", {blocked.Count} blocked (onboarding)" : "";
            await _progress.UpdateAsync(new RunProgress
            {
                RunId = runId, PercentComplete = 100, Processed = processed,
                Total = total, Failed = failed,
                StatusMessage = $"Complete — {processed} calculated, {failed} failed{blockedMsg}",
                RunStatus = failed == total && total > 0 ? "FAILED" : "CALCULATED",
                UpdatedAt = wallClock.UtcNow
            });
            _logger.LogInformation(
                "Run {RunId}: complete — {Processed} calculated, {Failed} failed, {Blocked} blocked (onboarding)",
                runId, processed, failed, blocked.Count);
        }
        catch (OperationCanceledException)
        {
            await runRepo.UpdateStatusAsync(runId, cancelledStatus, run.InitiatedBy);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId} failed", runId);
            await runRepo.UpdateStatusAsync(runId, failedStatus, run.InitiatedBy);
        }
    }

    // Resolves the run's employee population and the blocked set to flag as exceptions.
    // Full-context run: pay all active+cleared employees and flag every blocked employee in the
    // context (unchanged). Scoped run (Phase 12.6): narrow both to the run_scope's validated
    // target list — a supplemental/catch-up run pays only its chosen stragglers, never the whole
    // context. Targets are validated to belong to the run's payroll context (legal-entity scoping).
    private static async Task<(IReadOnlyList<Guid> Population, IReadOnlyList<Guid> Blocked)> ResolvePopulationAsync(
        PayrollRun run, IPayrollProfileRepository profileRepo, IPayrollRunRepository runRepo, ILookupCache lookup)
    {
        var eligible = await profileRepo.GetActiveEmploymentIdsByContextAsync(run.PayrollContextId);
        var blocked  = await profileRepo.GetActiveBlockedEmploymentIdsByContextAsync(run.PayrollContextId);

        if (run.RunScopeId is not Guid scopeId)
            return (eligible, blocked);  // full-context run — unchanged behaviour

        var scope = await runRepo.GetRunScopeAsync(scopeId)
            ?? throw new InvalidOperationException(
                $"Run {run.RunId} references run_scope {scopeId} which was not found.");

        var targets = ParseScopeTargets(scope, lookup);

        // Reject any target not enrolled in this run's payroll context — a scoped run must never
        // reach across pay groups / legal entities (the application's core scoping invariant).
        var enrolled  = (await profileRepo.GetEnrolledEmploymentIdsByContextAsync(run.PayrollContextId)).ToHashSet();
        var outsiders = targets.Where(t => !enrolled.Contains(t)).ToList();
        if (outsiders.Count > 0)
            throw new InvalidOperationException(
                $"Run {run.RunId}: run_scope {scopeId} targets {outsiders.Count} employment(s) not enrolled " +
                $"in payroll context {run.PayrollContextId}: {string.Join(", ", outsiders.Take(5))}" +
                (outsiders.Count > 5 ? " …" : ""));

        var targetSet = targets.ToHashSet();
        return (eligible.Where(targetSet.Contains).ToList(),
                blocked.Where(targetSet.Contains).ToList());
    }

    // EXPLICIT / EXCEPTION population methods carry a JSON array of employment_id strings (the
    // resolved target list). QUERY-method scopes aren't produced by the current UI / unsupported.
    private static IReadOnlyList<Guid> ParseScopeTargets(RunScope scope, ILookupCache lookup)
    {
        var method = lookup.GetCode(LookupTables.PopulationMethod, scope.PopulationMethodId);
        if (method is not ("EXPLICIT" or "EXCEPTION"))
            throw new NotSupportedException(
                $"run_scope {scope.RunScopeId}: population_method '{method}' is not supported yet (EXPLICIT / EXCEPTION only).");

        List<string>? raw;
        try { raw = JsonSerializer.Deserialize<List<string>>(scope.PopulationDefinition); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"run_scope {scope.RunScopeId}: population_definition is not a valid JSON id array.", ex);
        }

        return (raw ?? [])
            .Select(s => Guid.TryParse(s, out var g)
                ? g
                : throw new InvalidOperationException(
                    $"run_scope {scope.RunScopeId}: population_definition contains a non-GUID entry '{s}'."))
            .Distinct()
            .ToList();
    }

    // -------------------------------------------------------------
    // Approval path — ADR-017 (Phase 12.5.3)
    // -------------------------------------------------------------
    // Posts accumulator impacts for every succeeded employee result on the
    // run, then transitions the run to Approved. Mirrors the existing
    // Releasing → Released backgrounding pattern. Idempotent on two levels:
    //   - Run level: refetch and confirm status is still Approving; bail if
    //     a concurrent fire has already advanced past it.
    //   - Per-result level: skip any result whose impacts already exist
    //     (handles partial-post + retry).
    // -------------------------------------------------------------
    private async Task ProcessApproveAsync(ILifetimeScope scope, PayrollRun run, ILookupCache lookup, CancellationToken ct)
    {
        var runRepo         = scope.Resolve<IPayrollRunRepository>();
        var resultRepo      = scope.Resolve<IEmployeePayrollResultRepository>();
        var accumulatorRepo = scope.Resolve<IAccumulatorRepository>();
        var accumulator     = scope.Resolve<IAccumulatorService>();
        var wallClock       = scope.Resolve<IWallClock>();

        var approvingStatus        = lookup.GetId(LookupTables.RunStatus, "APPROVING");
        var approvedStatus         = lookup.GetId(LookupTables.RunStatus, "APPROVED");
        var calculatedResultStatus = lookup.GetId(LookupTables.EmployeeResultStatus, "CALCULATED");
        var approvedResultStatus   = lookup.GetId(LookupTables.EmployeeResultStatus, "APPROVED");

        // Run-level idempotency: a second concurrent dequeue of the same
        // run id finds the status already past Approving and exits silently.
        var fresh = await runRepo.GetByIdAsync(run.RunId);
        if (fresh is null || fresh.RunStatusId != approvingStatus)
        {
            _logger.LogInformation(
                "Run {RunId} not in Approving (status {Status}) — approval job exiting silently",
                run.RunId, fresh?.RunStatusId);
            return;
        }

        // Succeeded employee results only — failed/excluded leave no ledger
        // entry per ADR-017 §4d.
        var allResults = await resultRepo.GetByRunIdAsync(run.RunId);
        var succeeded  = allResults.Where(r => r.ResultStatusId == calculatedResultStatus).ToList();
        var total      = succeeded.Count;

        _logger.LogInformation("Run {RunId}: approving — posting accumulators for {Total} result(s)",
            run.RunId, total);

        await _progress.UpdateAsync(new RunProgress
        {
            RunId = run.RunId, PercentComplete = 0, Processed = 0, Total = total,
            Failed = 0, StatusMessage = $"Posting accumulators for {total} result(s)…",
            RunStatus = "APPROVING", UpdatedAt = wallClock.UtcNow
        });

        int posted  = 0;
        int skipped = 0;

        try
        {
            foreach (var result in succeeded)
            {
                ct.ThrowIfCancellationRequested();

                // Per-result idempotency: skip if impacts already exist.
                if (await accumulatorRepo.AnyImpactsForResultAsync(result.EmployeePayrollResultId))
                {
                    skipped++;
                }
                else
                {
                    await accumulator.ApplyAsync(result, run.RunId, ct);
                    // Result-level status mirrors the run.
                    await resultRepo.UpdateStatusAsync(result.EmployeePayrollResultId, approvedResultStatus);
                    posted++;
                }

                if (total > 0 && (posted + skipped) % 10 == 0)
                {
                    var pct = (int)((posted + skipped) * 100.0 / total);
                    await _progress.UpdateAsync(new RunProgress
                    {
                        RunId = run.RunId, PercentComplete = pct, Processed = posted,
                        Total = total, Failed = 0,
                        StatusMessage = $"Posted {posted} of {total}…",
                        RunStatus = "APPROVING", UpdatedAt = wallClock.UtcNow
                    });
                }
            }

            await runRepo.UpdateStatusAsync(run.RunId, approvedStatus, fresh.LastUpdatedBy);

            await _progress.UpdateAsync(new RunProgress
            {
                RunId = run.RunId, PercentComplete = 100, Processed = posted,
                Total = total, Failed = 0,
                StatusMessage = $"Approved — {posted} posted, {skipped} skipped (already posted)",
                RunStatus = "APPROVED", UpdatedAt = wallClock.UtcNow
            });
            _logger.LogInformation("Run {RunId}: approved — {Posted} posted, {Skipped} skipped (idempotent)",
                run.RunId, posted, skipped);

            // Period Reset Audit (ADR-020 / Phase 12.9): this run has now committed in its
            // reset boundary, so materialize any just-closed prior-boundary resets for the
            // context (audit-only, idempotent). The service swallows its own errors — it
            // must never undo a completed approval.
            var resetService = scope.Resolve<IAccumulatorResetService>();
            await resetService.DetectAndRecordResetsAsync(run.PayrollContextId, run.PayDate, ct);
        }
        catch (OperationCanceledException)
        {
            // On cancellation, leave the run in Approving — re-enqueue or
            // manual recovery will pick it up where it left off (idempotency
            // guard handles the partial-post case).
            _logger.LogWarning("Run {RunId}: approval cancelled mid-post — {Posted} posted, run remains in Approving",
                run.RunId, posted);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId}: approval failed mid-post — {Posted} posted, run remains in Approving",
                run.RunId, posted);
            // Leave the run in Approving so the operator can investigate; do
            // not flip to Failed (calc succeeded; only the post is broken).
            throw;
        }
    }
}
