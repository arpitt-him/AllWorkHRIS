using System.Threading.Channels;
using Autofac;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Payroll.Domain.Results;
using AllWorkHRIS.Module.Payroll.Domain.ResultSet;
using AllWorkHRIS.Module.Payroll.Domain.Run;
using AllWorkHRIS.Module.Payroll.Repositories;
using AllWorkHRIS.Module.Payroll.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll.Jobs;

/// <summary>
/// Long-running background service that dequeues payroll run IDs and
/// drives each through the full calculation lifecycle.
/// A child lifetime scope is created per run so scoped services
/// (repositories, engine) resolve correctly from a singleton.
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
        var accumulator   = scope.Resolve<IAccumulatorService>();

        var run = await runRepo.GetByIdAsync(runId);
        if (run is null)
        {
            _logger.LogWarning("Run {RunId} not found — skipping", runId);
            return;
        }

        // Release path — no calculation needed, just finalise
        if (run.RunStatusId == (int)PayrollRunStatus.Releasing)
        {
            await runRepo.UpdateStatusAsync(runId, (int)PayrollRunStatus.Released, run.InitiatedBy);
            await contextRepo.UpdatePeriodStatusAsync(run.PeriodId, "CLOSED", run.InitiatedBy);
            _logger.LogInformation("Run {RunId} released; period {PeriodId} closed", runId, run.PeriodId);
            return;
        }

        // Transition to CALCULATING
        var startTime = DateTimeOffset.UtcNow;
        await runRepo.UpdateStatusAsync(runId, (int)PayrollRunStatus.Calculating, run.InitiatedBy);
        await runRepo.SetRunTimestampsAsync(runId, startTime, null, run.InitiatedBy);

        try
        {
            // Create result set for this calculation pass
            var now = DateTimeOffset.UtcNow;
            var resultSet = new PayrollRunResultSet
            {
                PayrollRunResultSetId        = Guid.NewGuid(),
                PayrollRunId                 = runId,
                RunScopeId                   = null,
                SourcePeriodId               = run.PeriodId,
                ExecutionPeriodId            = run.PeriodId,
                ParentPayrollRunResultSetId  = null,
                RootPayrollRunResultSetId    = null,
                ResultSetLineageSequence     = 1,
                CorrectionReferenceId        = null,
                ResultSetStatusId            = (int)ResultSetStatus.Pending,
                ResultSetTypeId              = 1,
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

            // Resolve pay frequency and period dates (same for all employees in the run)
            var periodsPerYear = await contextRepo.GetPeriodsPerYearAsync(run.PayrollContextId);
            var period         = await contextRepo.GetPeriodByIdAsync(run.PeriodId)
                                 ?? throw new InvalidOperationException($"Period {run.PeriodId} not found for run {runId}");

            // Resolve the employee population for this run via payroll_profile
            var population = await ResolvePopulationAsync(run, profileRepo, ct);
            var total      = population.Count;

            // Record employees blocked by incomplete onboarding tasks
            var blocked = await profileRepo.GetActiveBlockedEmploymentIdsByContextAsync(run.PayrollContextId);
            if (blocked.Count > 0)
            {
                var exceptionTime = DateTimeOffset.UtcNow;
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
                RunStatus = "CALCULATING", UpdatedAt = DateTimeOffset.UtcNow
            });

            int processed = 0;
            int failed    = 0;

            foreach (var employmentId in population)
            {
                ct.ThrowIfCancellationRequested();

                var resultId   = Guid.NewGuid();
                var resultTime = DateTimeOffset.UtcNow;

                // Create the employee result header row before writing result lines
                var employeeResult = new EmployeePayrollResult
                {
                    EmployeePayrollResultId          = resultId,
                    PayrollRunResultSetId             = resultSet.PayrollRunResultSetId,
                    PayrollRunId                      = runId,
                    RunScopeId                        = null,
                    EmploymentId                      = employmentId,
                    PersonId                          = Guid.Empty, // TODO: resolve via PayrollProfile
                    PayrollContextId                  = run.PayrollContextId,
                    SourcePeriodId                    = run.PeriodId,
                    ExecutionPeriodId                 = run.PeriodId,
                    ParentEmployeePayrollResultId     = null,
                    RootEmployeePayrollResultId       = null,
                    ResultLineageSequence             = 1,
                    CorrectionReferenceId             = null,
                    ResultStatusId                    = 1,   // CALCULATING
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

                var annualEquivalent = await compSnapshot.GetAnnualEquivalentAsync(employmentId, run.PayDate);

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
                    AnnualEquivalent        = annualEquivalent,
                    PeriodsPerYear          = periodsPerYear
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
                    await resultRepo.UpdateStatusAsync(resultId, 2); // CALCULATED

                    await accumulator.ApplyAsync(
                        employeeResult with
                        {
                            GrossPayAmount                  = output.GrossPay,
                            TotalDeductionsAmount           = output.TotalDeductionsAmount,
                            TotalEmployeeTaxAmount          = output.TotalEmployeeTaxAmount,
                            TotalEmployerContributionAmount = output.TotalEmployerContribAmount,
                            NetPayAmount                    = output.NetPay,
                            ResultStatusId                  = 2   // CALCULATED
                        },
                        runId, ct);

                    processed++;
                }
                else
                {
                    await resultRepo.UpdateStatusAsync(resultId, 10); // FAILED
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
                        RunStatus = "CALCULATING", UpdatedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            var finalStatus = total > 0 && failed == total
                ? (int)PayrollRunStatus.Failed
                : (int)PayrollRunStatus.Calculated;

            await runRepo.UpdateStatusAsync(runId, finalStatus, run.InitiatedBy);
            await runRepo.SetRunTimestampsAsync(runId, startTime, DateTimeOffset.UtcNow, run.InitiatedBy);
            await resultSetRepo.UpdateStatusAsync(resultSet.PayrollRunResultSetId, (int)ResultSetStatus.Calculated);

            var blockedMsg = blocked.Count > 0 ? $", {blocked.Count} blocked (onboarding)" : "";
            await _progress.UpdateAsync(new RunProgress
            {
                RunId = runId, PercentComplete = 100, Processed = processed,
                Total = total, Failed = failed,
                StatusMessage = $"Complete — {processed} calculated, {failed} failed{blockedMsg}",
                RunStatus = failed == total && total > 0 ? "FAILED" : "CALCULATED",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            _logger.LogInformation(
                "Run {RunId}: complete — {Processed} calculated, {Failed} failed, {Blocked} blocked (onboarding)",
                runId, processed, failed, blocked.Count);
        }
        catch (OperationCanceledException)
        {
            await runRepo.UpdateStatusAsync(runId, (int)PayrollRunStatus.Cancelled, run.InitiatedBy);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId} failed", runId);
            await runRepo.UpdateStatusAsync(runId, (int)PayrollRunStatus.Failed, run.InitiatedBy);
        }
    }

    private static Task<IReadOnlyList<Guid>> ResolvePopulationAsync(
        PayrollRun run, IPayrollProfileRepository profileRepo, CancellationToken ct)
        => profileRepo.GetActiveEmploymentIdsByContextAsync(run.PayrollContextId);
}
