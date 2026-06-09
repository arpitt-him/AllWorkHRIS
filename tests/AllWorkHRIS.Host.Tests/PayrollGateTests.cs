using System.Threading.Channels;
using Autofac;
using Dapper;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host.Hris.Commands;
using AllWorkHRIS.Host.Hris.Repositories;
using AllWorkHRIS.Host.Hris.Services;
using AllWorkHRIS.Module.Payroll.Commands;
using AllWorkHRIS.Module.Payroll.Domain.Events;
using AllWorkHRIS.Module.Payroll.Domain.Profile;
using AllWorkHRIS.Module.Payroll.Domain.Run;
using AllWorkHRIS.Module.Payroll.Jobs;
using AllWorkHRIS.Module.Payroll.Repositories;
using AllWorkHRIS.Module.Payroll.Services;
using AllWorkHRIS.Host.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Phase 4 gate tests for the Payroll module.
///
/// TC-PAY-001  InitiateRunAsync creates a DRAFT run in the DB
/// TC-PAY-002  Duplicate run for same period throws InvalidOperationException
/// TC-PAY-003  Job transitions status to CALCULATED after processing
/// TC-PAY-004  EmployeePayrollResult and REG earnings lines are persisted per employee
/// TC-PAY-011  Batch of employees all receive result rows; progress reaches 100 %
/// TC-PAY-020  HireEventHandler auto-creates payroll_profile with source AUTO_HIRE
/// TC-PAY-022  Blocked employee is excluded (exception row); included after gate cleared
/// TC-PAY-046  NON_EXEMPT worked+leave: OT computed on worked hours only; leave paid straight-time (ToDo #46)
///
/// Requires allworkhris_dev running locally.
/// </summary>
public sealed class PayrollGateTests : IDisposable
{
    // ---------------------------------------------------------------------------
    // Seed data — CORP-BW context + six sequential open periods
    // ---------------------------------------------------------------------------
    static readonly Guid ContextId = Guid.Parse("dd6ee25f-e02c-498e-8bd3-e5f397b40f4f");
    // Five sequential open CORP-BW periods with no prior runs
    static readonly Guid PeriodId1 = Guid.Parse("31d67929-0233-4005-b4ed-a2159fd99adb"); // TC-PAY-001/002
    static readonly Guid PeriodId2 = Guid.Parse("f64899f5-8fe0-4e92-96c3-e4c686c3d88c"); // TC-PAY-003/004
    static readonly Guid PeriodId3 = Guid.Parse("7cc2deee-3fcf-4b86-a090-03f70a9df9ac"); // TC-PAY-011
    static readonly Guid PeriodId4 = Guid.Parse("3f73bfc0-e016-462a-8460-8891914ff5ad"); // TC-PAY-022 run 1
    static readonly Guid PeriodId5 = Guid.Parse("812e483f-5415-4ad1-9cd9-7a54978b4eab"); // TC-PAY-022 run 2
    // TC-ACUM-011 cross-year reset: a 2025 period + two 2026 periods (see payroll_gate_test_fixture.sql)
    static readonly Guid AcumPeriod2025  = Guid.Parse("a0c12025-0000-4000-8000-000000000001");
    static readonly Guid AcumPeriod2026A = Guid.Parse("a0c12026-0000-4000-8000-00000000000a");
    static readonly Guid AcumPeriod2026B = Guid.Parse("a0c12026-0000-4000-8000-00000000000b");

    // HR seed references shared with HireEmployeeIntegrationTests
    static readonly Guid LegalEntityId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    static readonly Guid DepartmentId  = Guid.Parse("10000000-0000-0000-0000-000000000003");
    static readonly Guid LocationId    = Guid.Parse("10000000-0000-0000-0000-000000000004");
    static readonly Guid JobId         = Guid.Parse("20000000-0000-0000-0000-000000000001");

    // ---------------------------------------------------------------------------
    // Services
    // ---------------------------------------------------------------------------
    readonly IConnectionFactory        _connectionFactory;
    readonly IEmploymentService        _employmentService;
    readonly ILookupCache              _lookupCache;
    readonly IPayrollRunService        _runService;
    readonly IPayrollRunRepository     _runRepo;
    readonly IPayrollProfileRepository _profileRepo;

    // Cleanup tracking
    readonly List<Guid> _personIds     = [];
    readonly List<Guid> _employmentIds = [];
    readonly List<Guid> _runIds        = [];

    public PayrollGateTests()
    {
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
            "Host=localhost;Database=allworkhris_dev;Username=postgres;Password=dev");
        Environment.SetEnvironmentVariable("DATABASE_PROVIDER", "postgresql");

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new TestDateOnlyHandler());
        SqlMapper.AddTypeHandler(new TestNullableDateOnlyHandler());

        _connectionFactory = new ConnectionFactory();

        var cache = new LookupCache(_connectionFactory);
        cache.RefreshAsync().GetAwaiter().GetResult();
        _lookupCache = cache;

        // HRIS infrastructure (same wiring as HireEmployeeIntegrationTests)
        var personRepo        = new PersonRepository(_connectionFactory);
        var personAddressRepo = new PersonAddressRepository(_connectionFactory);
        var temporalCtx       = new AllWorkHRIS.Core.Temporal.SystemTemporalContext();
        var employmentRepo    = new EmploymentRepository(_connectionFactory, temporalCtx);
        var assignmentRepo    = new AssignmentRepository(_connectionFactory, temporalCtx);
        var compensationRepo  = new CompensationRepository(_connectionFactory, temporalCtx);
        var eventRepo         = new EmployeeEventRepository(_connectionFactory);
        var eventPublisher    = new InProcessEventBus();
        var temporalContext   = new SystemTemporalContext();
        var workQueueRepo     = new WorkQueueRepository(_connectionFactory);
        var workQueueService  = new WorkQueueService(workQueueRepo, new AllWorkHRIS.Core.Temporal.SystemTemporalContext());
        var onboardingRepo    = new OnboardingRepository(_connectionFactory);
        var onboardingService = new OnboardingService(
            _connectionFactory, onboardingRepo, workQueueService,
            eventPublisher, _lookupCache, temporalContext);

        _employmentService = new EmploymentService(
            _connectionFactory, personRepo, personAddressRepo, employmentRepo,
            assignmentRepo, compensationRepo, eventRepo, eventPublisher,
            temporalContext, _lookupCache, onboardingService);

        // Payroll infrastructure
        IAuditService auditService = new NullAuditService();
        _runRepo     = new PayrollRunRepository(_connectionFactory);
        _profileRepo = new PayrollProfileRepository(_connectionFactory, auditService);
        var contextRepo = new PayrollContextRepository(_connectionFactory, auditService);
        var queue       = Channel.CreateUnbounded<Guid>();

        // resultRepo re-wired into PayrollRunService for the ToDo #43 already-paid guard.
        // accumulatorRepo / resultLineRepo retained as locals in case future tests need them.
        var resultRepo = new EmployeePayrollResultRepository(_connectionFactory);
        _ = new AccumulatorRepository(_connectionFactory);
        _ = new ResultLineRepository(_connectionFactory);

        _runService = new PayrollRunService(
            _runRepo, contextRepo, resultRepo, queue,
            temporalCtx, NullLogger<PayrollRunService>.Instance, auditService, _lookupCache,
            new StandardHoursPayrollHoursSource(), new NullPayrollCorrectionService());
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-001: InitiateRunAsync creates a DRAFT run
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task InitiateRun_WithValidCommand_CreatesDraftRun()
    {
        var userId = Guid.NewGuid();
        var runId  = await _runService.InitiateRunAsync(new InitiatePayrollRunCommand
        {
            PayrollContextId = ContextId,
            PeriodId         = PeriodId1,
            RunTypeId        = 1, // REGULAR
            RunDescription   = "Gate test TC-PAY-001",
            InitiatedBy      = userId
        });
        _runIds.Add(runId);

        Assert.NotEqual(Guid.Empty, runId);

        var run = await _runRepo.GetByIdAsync(runId);
        Assert.NotNull(run);
        Assert.Equal(ContextId,                        run.PayrollContextId);
        Assert.Equal(PeriodId1,                        run.PeriodId);
        Assert.Equal(_lookupCache.GetId(LookupTables.RunStatus, "DRAFT"), run.RunStatusId);
        Assert.Equal(userId,                           run.InitiatedBy);
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-002: Duplicate run for same period throws
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task InitiateRun_DuplicatePeriod_ThrowsInvalidOperationException()
    {
        var userId = Guid.NewGuid();
        var runId  = await _runService.InitiateRunAsync(new InitiatePayrollRunCommand
        {
            PayrollContextId = ContextId,
            PeriodId         = PeriodId1,
            RunTypeId        = 1,
            InitiatedBy      = userId
        });
        _runIds.Add(runId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runService.InitiateRunAsync(new InitiatePayrollRunCommand
            {
                PayrollContextId = ContextId,
                PeriodId         = PeriodId1,
                RunTypeId        = 1,
                InitiatedBy      = userId
            }));
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-003 + TC-PAY-004: Job transitions to CALCULATED; result and
    // REG earnings line created for a salaried employee
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PayrollJob_CalculatesEmployee_PersistsResultAndRegEarningsLine()
    {
        var employmentId = await HireAndEnrollAsync("PAY003", blockingCleared: true);

        var runId = await _runService.InitiateRunAsync(new InitiatePayrollRunCommand
        {
            PayrollContextId = ContextId,
            PeriodId         = PeriodId2,
            RunTypeId        = 1,
            InitiatedBy      = Guid.NewGuid()
        });
        _runIds.Add(runId);

        var progress = await RunJobAsync(runId);

        // TC-PAY-003: run status must be CALCULATED
        var run = await _runRepo.GetByIdAsync(runId);
        Assert.NotNull(run);
        Assert.Equal(_lookupCache.GetId(LookupTables.RunStatus, "CALCULATED"), run.RunStatusId);

        // TC-PAY-004: our hired employee has a result row with a REG earnings line
        using var conn = _connectionFactory.CreateConnection();

        var resultId = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT employee_payroll_result_id
            FROM employee_payroll_result
            WHERE payroll_run_id = @RunId AND employment_id = @EmpId
            """,
            new { RunId = runId, EmpId = employmentId });

        Assert.NotNull(resultId);

        var regCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM earnings_result_line
            WHERE employee_payroll_result_id = @Id AND earnings_code = 'REG'
            """,
            new { Id = resultId });

        Assert.Equal(1, regCount);
        Assert.Equal(100, progress.PercentComplete);
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-011: Batch run — 10 employees; all receive result rows;
    // progress reaches 100 %
    // (The spec gate is 250 employees; 10 exercises the same batch loop
    // and progress-reporting code path.)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PayrollJob_BatchRun_AllEmployeesCalculatedAndProgressAt100()
    {
        const int count = 10;

        var employmentIds = new List<Guid>();
        for (int i = 0; i < count; i++)
            employmentIds.Add(await HireAndEnrollAsync($"PAY011-{i:D3}", blockingCleared: true));

        var runId = await _runService.InitiateRunAsync(new InitiatePayrollRunCommand
        {
            PayrollContextId = ContextId,
            PeriodId         = PeriodId3,
            RunTypeId        = 1,
            InitiatedBy      = Guid.NewGuid()
        });
        _runIds.Add(runId);

        var progress = await RunJobAsync(runId);

        // Every hired employment must appear in employee_payroll_result for this run
        using var conn = _connectionFactory.CreateConnection();
        foreach (var empId in employmentIds)
        {
            var exists = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM employee_payroll_result
                WHERE payroll_run_id = @RunId AND employment_id = @EmpId
                """,
                new { RunId = runId, EmpId = empId });

            Assert.Equal(1, exists);
        }

        Assert.Equal(100, progress.PercentComplete);
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-020: HireEventHandler auto-creates payroll_profile with AUTO_HIRE
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HireEventHandler_WithPayrollContextId_CreatesAutoHireProfile()
    {
        var command = BuildHireCommand("PAY020");
        var hired   = await _employmentService.HireEmployeeAsync(command);
        _personIds.Add(hired.PersonId);
        _employmentIds.Add(hired.EmploymentId);

        // Build a minimal Autofac container so HireEventHandler can resolve a child scope
        var auditService = new NullAuditService();
        var builder      = new ContainerBuilder();
        builder.RegisterInstance(_connectionFactory).As<IConnectionFactory>().SingleInstance();
        builder.RegisterInstance((IAuditService)auditService).As<IAuditService>().SingleInstance();
        builder.RegisterInstance((ITemporalContext)new SystemTemporalContext()).As<ITemporalContext>().SingleInstance();
        builder.RegisterType<PayrollProfileRepository>().As<IPayrollProfileRepository>().InstancePerLifetimeScope();
        await using var container = builder.Build();

        var handler = new HireEventHandler(container);
        await handler.HandleAsync(new HireEventPayload
        {
            EmploymentId     = hired.EmploymentId,
            PersonId         = hired.PersonId,
            EventId          = hired.EventId,
            TenantId         = Guid.NewGuid(),
            EffectiveDate    = new DateOnly(2024, 1, 15),
            LegalEntityId    = LegalEntityId,
            FlsaStatus       = "NON_EXEMPT",
            PayrollContextId = ContextId,
            EventTimestamp   = DateTimeOffset.UtcNow
        });

        var profileRepo = new PayrollProfileRepository(_connectionFactory, auditService);
        var profile     = await profileRepo.GetByEmploymentIdAsync(hired.EmploymentId);

        Assert.NotNull(profile);
        Assert.Equal(ContextId,   profile.PayrollContextId);
        Assert.Equal("ACTIVE",    profile.EnrollmentStatus);
        Assert.Equal("AUTO_HIRE", profile.EnrollmentSource);
        Assert.False(profile.BlockingTasksCleared);
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-022: Blocked employee excluded from run; included after gate cleared
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PayrollJob_BlockedEmployee_ExcludedThenIncludedAfterGateCleared()
    {
        // Hire with blocking_tasks_cleared = FALSE
        var command = BuildHireCommand("PAY022");
        var hired   = await _employmentService.HireEmployeeAsync(command);
        _personIds.Add(hired.PersonId);
        _employmentIds.Add(hired.EmploymentId);

        var now = DateTimeOffset.UtcNow;
        await _profileRepo.InsertAsync(new PayrollProfile
        {
            PayrollProfileId     = Guid.NewGuid(),
            EmploymentId         = hired.EmploymentId,
            PersonId             = hired.PersonId,
            PayrollContextId     = ContextId,
            EnrollmentStatus     = "ACTIVE",
            EffectiveStartDate   = new DateOnly(2024, 1, 15),
            EffectiveEndDate     = null,
            FinalPayFlag         = false,
            BlockingTasksCleared = false,
            EnrollmentSource     = "MANUAL",
            CreatedBy            = hired.EventId,
            CreationTimestamp    = now,
            LastUpdatedBy        = hired.EventId,
            LastUpdateTimestamp  = now
        });

        var userId = Guid.NewGuid();

        // -------- Run 1: employee is blocked --------
        var runId1 = await _runService.InitiateRunAsync(new InitiatePayrollRunCommand
        {
            PayrollContextId = ContextId,
            PeriodId         = PeriodId4,
            RunTypeId        = 1,
            InitiatedBy      = userId
        });
        _runIds.Add(runId1);

        await RunJobAsync(runId1);

        using var conn = _connectionFactory.CreateConnection();

        var exceptionCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM payroll_run_exception
            WHERE run_id = @RunId AND employment_id = @EmpId
            """,
            new { RunId = runId1, EmpId = hired.EmploymentId });

        Assert.Equal(1, exceptionCount);

        var resultCount1 = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM employee_payroll_result
            WHERE payroll_run_id = @RunId AND employment_id = @EmpId
            """,
            new { RunId = runId1, EmpId = hired.EmploymentId });

        Assert.Equal(0, resultCount1);

        // Run 1 is CALCULATED — still in-flight for the context. Discard it so a new run
        // can be initiated for the same context (ADR-017 Addendum A per-context in-flight
        // guard). Cancel-from-Calculated is a clean discard: no ledger was posted.
        await _runService.CancelRunAsync(new CancelPayrollRunCommand
        {
            RunId = runId1, CancelledBy = userId, Reason = "Gate test: discard run 1 before run 2"
        });

        // -------- Clear the onboarding gate --------
        await _profileRepo.SetBlockingTasksClearedAsync(hired.EmploymentId, userId);

        // -------- Run 2: employee is now unblocked --------
        var runId2 = await _runService.InitiateRunAsync(new InitiatePayrollRunCommand
        {
            PayrollContextId = ContextId,
            PeriodId         = PeriodId5,
            RunTypeId        = 1,
            InitiatedBy      = userId
        });
        _runIds.Add(runId2);

        await RunJobAsync(runId2);

        var resultCount2 = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM employee_payroll_result
            WHERE payroll_run_id = @RunId AND employment_id = @EmpId
            """,
            new { RunId = runId2, EmpId = hired.EmploymentId });

        Assert.Equal(1, resultCount2);
    }

    // ---------------------------------------------------------------------------
    // TC-ACUM-011: Period Reset Audit — a cross-year approval records a SYSTEM reset
    // for the just-closed boundary, with the correct closing balance; idempotent.
    // (ADR-020 / Phase 12.9. Requires migration 034.)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PeriodResetAudit_CrossYearApproval_RecordsSystemReset_Idempotently()
    {
        var employmentId = await HireAndEnrollAsync("ACUM011", blockingCleared: true);
        var userId       = Guid.NewGuid();

        // 2025 run: calculate + approve → posts 2025 accumulator balances (no prior-year reset).
        await CalculateAndApproveAsync(AcumPeriod2025, userId);

        // 2026 run: approval commits in the new boundary → detection materializes the 2025 reset.
        await CalculateAndApproveAsync(AcumPeriod2026A, userId);

        using var conn = _connectionFactory.CreateConnection();
        var rows = (await conn.QueryAsync(
            """
            SELECT closing_balance, opened_by, reset_source, reset_date
            FROM   accumulator_reset_audit
            WHERE  participant_id = @Emp AND reset_boundary_year = 2025
            """,
            new { Emp = employmentId })).ToList();

        // One SYSTEM reset row per CALENDAR_YEAR accumulator the employee accrued in 2025
        // (gross wages + the Phase-12.8 Medicare/SS wage bases), all with a real closing balance.
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("SYSTEM",    (string)r.opened_by));
        Assert.All(rows, r => Assert.Equal("AUTOMATIC", (string)r.reset_source));
        Assert.All(rows, r => Assert.True((decimal)r.closing_balance > 0m, "Closing balance should be the posted 2025 total"));
        var countAfterFirst = rows.Count;

        // Idempotency: a second 2026 cross-boundary approval must not duplicate the 2025 resets.
        await CalculateAndApproveAsync(AcumPeriod2026B, userId);
        var countAfterSecond = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accumulator_reset_audit WHERE participant_id = @Emp AND reset_boundary_year = 2025",
            new { Emp = employmentId });
        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-046: NON_EXEMPT with worked + paid-leave hours — OT is computed on the
    // WORKED hours only (FLSA, cf. 29 CFR 778.218), while the paid leave is still PAID
    // at straight time (folded into REG). Phase 12.12 / ToDo #46 / ADR-023.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PayrollJob_NonExemptWorkedPlusLeave_OvertimeOnWorkedOnly_LeavePaidStraightTime()
    {
        var employmentId = await HireAndEnrollAsync("PAY046", blockingCleared: true);

        var runId = await _runService.InitiateRunAsync(new InitiatePayrollRunCommand
        {
            PayrollContextId = ContextId,
            PeriodId         = PeriodId2,
            RunTypeId        = 1,
            InitiatedBy      = Guid.NewGuid()
        });
        _runIds.Add(runId);

        // 44 worked + 8 paid-leave hours in one workweek. Expected (FLSA / Phase 12.12):
        //   worked 44  → 40 REG-worked + 4 OT;  leave 8 → straight time, folded into REG.
        //   ⇒ REG quantity = 48, OT quantity = 4.
        // This single result discriminates against BOTH failure modes:
        //   • dropping leave (naive is_worked_time filter on the sole pay path) → REG = 40
        //   • counting leave toward OT (the pre-fix all-categories basis)       → OT  = 12
        var hours = new FixedHoursPayrollHoursSource(
            worked: [(new DateOnly(2024, 1, 15), 44m)],
            nonWorkedPayableHours: 8m);

        await RunJobAsync(runId, hours);

        using var conn = _connectionFactory.CreateConnection();

        var resultId = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT employee_payroll_result_id FROM employee_payroll_result
            WHERE payroll_run_id = @RunId AND employment_id = @EmpId
            """,
            new { RunId = runId, EmpId = employmentId });
        Assert.NotNull(resultId);

        var regQty = await conn.ExecuteScalarAsync<decimal?>(
            "SELECT quantity FROM earnings_result_line WHERE employee_payroll_result_id = @Id AND earnings_code = 'REG'",
            new { Id = resultId });
        var otQty = await conn.ExecuteScalarAsync<decimal?>(
            "SELECT quantity FROM earnings_result_line WHERE employee_payroll_result_id = @Id AND earnings_code = 'OT'",
            new { Id = resultId });

        Assert.Equal(48m, regQty);   // 40 worked-reg + 8 paid leave, at straight time (leave still paid)
        Assert.Equal(4m,  otQty);    // OT only from the 44 worked hours (44 − 40); leave excluded from the threshold
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-REVERSE: ADR-027 Increment 1 — reversing an APPROVED run contra-posts
    // its accumulator impacts (restoring the PRIOR-run YTD, not zero), marks the
    // run/results REVERSED, reopens the period, nets the run's impacts to zero, and
    // is idempotent. Requires migration 050 (REVERSED run status).
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ReverseApprovedRun_RestoresPriorYtd_ReopensPeriod_Idempotent()
    {
        var emp    = await HireAndEnrollAsync("REV-YTD", blockingCleared: true);
        var userId = Guid.NewGuid();

        using var conn = _connectionFactory.CreateConnection();
        decimal Ytd() => conn.ExecuteScalar<decimal>(
            "SELECT COALESCE(SUM(current_value), 0) FROM accumulator_balance WHERE participant_id = @Emp",
            new { Emp = emp });

        // Run 1 (2026 period A) → APPROVED. Its YTD is the "prior" a reversal of run 2 must restore.
        await CalculateAndApproveAsync(AcumPeriod2026A, userId);
        var ytdAfterRun1 = Ytd();
        Assert.True(ytdAfterRun1 > 0m, "run 1 should post a positive YTD");

        // Run 2 (2026 period B, same year — no reset) → APPROVED. YTD grows.
        var run2 = await CalculateAndApproveAsync(AcumPeriod2026B, userId);
        Assert.True(Ytd() > ytdAfterRun1, "run 2 should add to the YTD");

        // Reverse run 2 via the real correction service.
        var correction = BuildCorrectionService();
        var outcome = await correction.ReverseRunAsync(
            new ReverseRunRequest { RunId = run2, ReversedBy = userId, Reason = "integration test" });
        Assert.False(outcome.AlreadyReversed);
        Assert.NotEmpty(outcome.ReversedResultIds);

        // The key proof: YTD falls back to run 1's value — the PRIOR cumulative, NOT zero
        // (run 1's contribution survives; only run 2's per-period balance is reverted).
        Assert.Equal(ytdAfterRun1, Ytd());

        // Run 2 is REVERSED and its period reopens for a fresh Regular run.
        var run2Row = await _runRepo.GetByIdAsync(run2);
        Assert.Equal(_lookupCache.GetId(LookupTables.RunStatus, "REVERSED"), run2Row!.RunStatusId);
        Assert.Null(await _runRepo.GetActiveRegularRunForPeriodAsync(AcumPeriod2026B));

        // Run 2's impacts net to zero (original postings + their negating reversals — forward-only).
        var netDelta = conn.ExecuteScalar<decimal>(
            "SELECT COALESCE(SUM(delta_value), 0) FROM accumulator_impact WHERE payroll_run_id = @Id",
            new { Id = run2 });
        Assert.Equal(0m, netDelta);

        // Idempotent — a second reverse is a no-op.
        var again = await correction.ReverseRunAsync(
            new ReverseRunRequest { RunId = run2, ReversedBy = userId, Reason = "again" });
        Assert.True(again.AlreadyReversed);
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-REVERSE-2: ADR-027 2a — per-EE (result-level) reversal. Reversing one
    // employee of a multi-employee run contra-posts ONLY that employee's impacts and
    // marks ONLY that result REVERSED; the others stand and the run STAYS APPROVED
    // (so the period is still covered). Requires migration 050.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ReversePerEmployee_ReversesOnlySelected_RunStaysApproved()
    {
        var emp1   = await HireAndEnrollAsync("REV-A", blockingCleared: true);
        var emp2   = await HireAndEnrollAsync("REV-B", blockingCleared: true);
        var userId = Guid.NewGuid();

        var runId = await CalculateAndApproveAsync(AcumPeriod2026A, userId);

        using var conn = _connectionFactory.CreateConnection();
        int StatusOf(Guid emp) => conn.ExecuteScalar<int>(
            "SELECT result_status_id FROM employee_payroll_result WHERE payroll_run_id = @Run AND employment_id = @Emp",
            new { Run = runId, Emp = emp });
        decimal NetImpacts(Guid emp) => conn.ExecuteScalar<decimal>(
            "SELECT COALESCE(SUM(delta_value), 0) FROM accumulator_impact WHERE payroll_run_id = @Run AND employment_id = @Emp",
            new { Run = runId, Emp = emp });

        var approvedResult = _lookupCache.GetId(LookupTables.EmployeeResultStatus, "APPROVED");
        var reversedResult = _lookupCache.GetId(LookupTables.EmployeeResultStatus, "REVERSED");

        // Reverse ONLY emp1.
        var correction = BuildCorrectionService();
        var outcome = await correction.ReverseResultsAsync(new ReverseResultsRequest
            { RunId = runId, EmploymentIds = new[] { emp1 }, ReversedBy = userId, Reason = "per-EE test" });

        Assert.Single(outcome.ReversedResultIds);
        Assert.Equal(reversedResult, StatusOf(emp1));   // emp1 reversed
        Assert.Equal(approvedResult, StatusOf(emp2));   // emp2 untouched

        // The run stays APPROVED (emp2 still standing) — the period is NOT reopened.
        var run = await _runRepo.GetByIdAsync(runId);
        Assert.Equal(_lookupCache.GetId(LookupTables.RunStatus, "APPROVED"), run!.RunStatusId);
        Assert.NotNull(await _runRepo.GetActiveRegularRunForPeriodAsync(AcumPeriod2026A));

        // emp1's impacts net to zero (contra-posted); emp2's stand.
        Assert.Equal(0m, NetImpacts(emp1));
        Assert.True(NetImpacts(emp2) > 0m, "emp2's impacts should remain standing");
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-REPAY-SAMEPERIOD: posting a re-pay into a period that ALREADY carries a
    // balance row for the employee (the reverse-then-re-pay path) must upsert that
    // existing per-(EE,period) balance in place — no PK collision, no duplicate row.
    // Regression guard for the 23505 accumulator_balance_pkey failure that occurred
    // when the balance upsert relied on a NULLS-NOT-DISTINCT ON CONFLICT arbiter that
    // does not fire for employee-scoped rows (employer_id IS NULL). The explicit
    // UPDATE-by-accumulator_id-else-INSERT posts cleanly regardless of the index.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RepayAfterReverse_PostsIntoExistingSamePeriodBalance_UpsertsSingleRow()
    {
        var emp    = await HireAndEnrollAsync("REPAY-SP", blockingCleared: true);
        var userId = Guid.NewGuid();

        using var conn = _connectionFactory.CreateConnection();
        int BalanceRows() => conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM accumulator_balance WHERE participant_id = @Emp", new { Emp = emp });
        decimal Ytd() => conn.ExecuteScalar<decimal>(
            "SELECT COALESCE(SUM(current_value), 0) FROM accumulator_balance WHERE participant_id = @Emp",
            new { Emp = emp });

        // Run 1 (period A) → APPROVED: creates the per-(EE,period) balance rows.
        var run1          = await CalculateAndApproveAsync(AcumPeriod2026A, userId);
        var rowsAfterRun1 = BalanceRows();
        var ytdAfterRun1  = Ytd();
        Assert.True(rowsAfterRun1 > 0, "run 1 should create balance rows");
        Assert.True(ytdAfterRun1 > 0m, "run 1 should post a positive YTD");

        // Reverse run 1 → balance rows are REVERTED (not deleted: they persist at the prior
        // value), and the period reopens for a fresh run. This is what leaves a pre-existing
        // same-period balance for the re-pay to post into.
        var correction = BuildCorrectionService();
        await correction.ReverseRunAsync(
            new ReverseRunRequest { RunId = run1, ReversedBy = userId, Reason = "repay setup" });
        Assert.Equal(rowsAfterRun1, BalanceRows());                          // rows still present, just reverted
        Assert.Null(await _runRepo.GetActiveRegularRunForPeriodAsync(AcumPeriod2026A));

        // Re-pay: a SECOND run into the SAME period posts into those PRE-EXISTING balance rows
        // (the path that regressed). It must approve and must NOT create duplicate rows.
        var run2    = await CalculateAndApproveAsync(AcumPeriod2026A, userId);
        var run2Row = await _runRepo.GetByIdAsync(run2);

        Assert.Equal(_lookupCache.GetId(LookupTables.RunStatus, "APPROVED"), run2Row!.RunStatusId); // no PK collision
        Assert.Equal(rowsAfterRun1, BalanceRows());                          // upserted in place — no duplicate rows
        Assert.Equal(ytdAfterRun1,  Ytd());                                  // re-paid YTD restored, not doubled
    }

    // ---------------------------------------------------------------------------
    // TC-PAY-ORDERED-COMMIT: ADR-027 D8 — runs in one context finalize in pay-date
    // order. A later-pay-date run cannot RELEASE while an earlier-pay-date run in the
    // same context is not yet released; cannot APPROVE while an earlier is un-approved;
    // a same-date Supplemental finalizes after its Regular; void (REVERSED) earlier runs
    // don't block. (The screenshot case: Feb-13 Released while Jan-30 only Approved.)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ReleaseOutOfPayDateOrder_IsBlocked_UntilEarlierReleased()
    {
        await HireAndEnrollAsync("ORD-REL", blockingCleared: true);
        var userId = Guid.NewGuid();

        // Two runs in one context, both driven to APPROVED: A (pay 2026-06-19) earlier than B (pay 2026-07-19).
        var runA = await CalculateAndApproveAsync(AcumPeriod2026A, userId);
        var runB = await CalculateAndApproveAsync(AcumPeriod2026B, userId);

        // Releasing the LATER run while the EARLIER is only APPROVED is blocked (the reported bug).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _runService.ReleaseRunAsync(new ReleasePayrollRunCommand { RunId = runB, ReleasedBy = userId }));
        Assert.Contains("must be released first", ex.Message);
        Assert.Contains("earlier pay date", ex.Message);

        // Release the earlier run first → the later one is then free to release.
        await ReleaseRunToCompletionAsync(runA, userId);
        await ReleaseRunToCompletionAsync(runB, userId);

        var releasedId = _lookupCache.GetId(LookupTables.RunStatus, "RELEASED");
        Assert.Equal(releasedId, (await _runRepo.GetByIdAsync(runA))!.RunStatusId);
        Assert.Equal(releasedId, (await _runRepo.GetByIdAsync(runB))!.RunStatusId);
    }

    [Fact]
    public async Task ApproveOutOfPayDateOrder_IsBlocked_UnlessEarlierRunIsVoid()
    {
        var userId = Guid.NewGuid();
        var t0     = DateTimeOffset.UtcNow;

        // An earlier-pay-date sibling left un-approved (CALCULATED) + a later-pay-date target also CALCULATED.
        // Inserted directly because the single-in-flight guard won't allow two un-approved runs via InitiateRunAsync.
        var earlier = await InsertBareRunAsync(AcumPeriod2026A, new DateOnly(2026, 6, 19), "REGULAR", "CALCULATED", "ORD-A", t0);
        var target  = await InsertBareRunAsync(AcumPeriod2026B, new DateOnly(2026, 7, 19), "REGULAR", "CALCULATED", "ORD-B", t0.AddMinutes(1));

        // Approving the later run is blocked while the earlier is un-approved.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _runService.ApproveRunAsync(new ApprovePayrollRunCommand { RunId = target, ApprovedBy = userId }));
        Assert.Contains("must be approved first", ex.Message);
        Assert.Contains("earlier pay date", ex.Message);
        Assert.NotNull(await _runService.GetOrderedCommitBlockAsync(target));

        // A VOID earlier run (REVERSED) does not block — the gate skips CANCELLED/REVERSED/FAILED.
        await _runRepo.UpdateStatusAsync(earlier, _lookupCache.GetId(LookupTables.RunStatus, "REVERSED"), userId);
        Assert.Null(await _runService.GetOrderedCommitBlockAsync(target));
    }

    [Fact]
    public async Task SameDateSupplemental_MustFinalizeAfterItsRegular()
    {
        var userId  = Guid.NewGuid();
        var t0      = DateTimeOffset.UtcNow;
        var payDate = new DateOnly(2026, 6, 19);

        // Same pay date: a Regular still only APPROVED, and a Supplemental also APPROVED.
        var regular = await InsertBareRunAsync(AcumPeriod2026A, payDate, "REGULAR",      "APPROVED", "SD-REG",  t0);
        var supp    = await InsertBareRunAsync(AcumPeriod2026A, payDate, "SUPPLEMENTAL", "APPROVED", "SD-SUPP", t0.AddMinutes(1));

        // The Supplemental can't release until its same-date Regular releases (Regular sorts first),
        // and the message must explain the same-date run-type tiebreak (not just "pay-date order").
        var block = await _runService.GetOrderedCommitBlockAsync(supp);
        Assert.NotNull(block);
        Assert.Contains("same pay date", block);
        Assert.Contains("Regular run finalizes before", block);
        // The Regular is free to release — nothing earlier in the order.
        Assert.Null(await _runService.GetOrderedCommitBlockAsync(regular));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    // Drives a run DRAFT → CALCULATED → APPROVED (the approve pass posts accumulators
    // and triggers reset detection). RunJobAsync processes whatever status the run is in.
    private async Task<Guid> CalculateAndApproveAsync(Guid periodId, Guid userId)
    {
        var runId = await _runService.InitiateRunAsync(new InitiatePayrollRunCommand
        {
            PayrollContextId = ContextId, PeriodId = periodId, RunTypeId = 1, InitiatedBy = userId
        });
        _runIds.Add(runId);

        await RunJobAsync(runId);                                   // DRAFT → CALCULATED
        await _runService.ApproveRunAsync(new ApprovePayrollRunCommand { RunId = runId, ApprovedBy = userId });
        await RunJobAsync(runId);                                   // APPROVING → APPROVED (+ reset detection)
        return runId;
    }

    // Releases an APPROVED run all the way to RELEASED (RELEASING → RELEASED via the job).
    private async Task ReleaseRunToCompletionAsync(Guid runId, Guid userId)
    {
        await _runService.ReleaseRunAsync(new ReleasePayrollRunCommand { RunId = runId, ReleasedBy = userId });
        await DriveJobAsync(runId);                                 // RELEASING → RELEASED (emits no progress)
    }

    // Inserts a bare run row directly (no results), bypassing InitiateRunAsync's single-in-flight
    // guard — needed to construct ordered-commit scenarios the normal flow won't allow.
    private async Task<Guid> InsertBareRunAsync(
        Guid periodId, DateOnly payDate, string runTypeCode, string statusCode, string description, DateTimeOffset created)
    {
        var runId  = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await _runRepo.InsertAsync(new PayrollRun
        {
            RunId                      = runId,
            PayrollContextId           = ContextId,
            PeriodId                   = periodId,
            PayDate                    = payDate,
            RunTypeId                  = _lookupCache.GetId(LookupTables.RunType, runTypeCode),
            RunStatusId                = _lookupCache.GetId(LookupTables.RunStatus, statusCode),
            RunDescription             = description,
            ParentRunId                = null,
            RelatedRunGroupId          = null,
            RunScopeId                 = null,
            RuleAndConfigVersionRef    = null,
            TemporalOverrideActiveFlag = false,
            TemporalOverrideDate       = null,
            InitiatedBy                = userId,
            RunStartTimestamp          = null,
            RunEndTimestamp            = null,
            CreatedBy                  = userId,
            CreationTimestamp          = created,
            LastUpdatedBy              = userId,
            LastUpdateTimestamp        = created
        });
        _runIds.Add(runId);
        return runId;
    }

    // ADR-027 Increment 1: a real PayrollCorrectionService over the shared test composition (the gate's
    // _runService uses a no-op correction double). Resolves the real AccumulatorService + repos so a
    // reversal actually contra-posts against the DB.
    private PayrollCorrectionService BuildCorrectionService()
    {
        var container = PayrollRunTestContainer.Build(_connectionFactory, _lookupCache);
        return new PayrollCorrectionService(
            container.Resolve<IPayrollRunRepository>(),
            container.Resolve<IEmployeePayrollResultRepository>(),
            container.Resolve<IAccumulatorService>(),
            container.Resolve<IPayrollHoursSource>(),
            _lookupCache,
            new NullAuditService(),
            NullLogger<PayrollCorrectionService>.Instance);
    }

    private async Task<Guid> HireAndEnrollAsync(string tag, bool blockingCleared)
    {
        var command = BuildHireCommand(tag);
        var result  = await _employmentService.HireEmployeeAsync(command);
        _personIds.Add(result.PersonId);
        _employmentIds.Add(result.EmploymentId);

        var now = DateTimeOffset.UtcNow;
        await _profileRepo.InsertAsync(new PayrollProfile
        {
            PayrollProfileId     = Guid.NewGuid(),
            EmploymentId         = result.EmploymentId,
            PersonId             = result.PersonId,
            PayrollContextId     = ContextId,
            EnrollmentStatus     = "ACTIVE",
            EffectiveStartDate   = new DateOnly(2024, 1, 15),
            EffectiveEndDate     = null,
            FinalPayFlag         = false,
            BlockingTasksCleared = blockingCleared,
            EnrollmentSource     = "MANUAL",
            CreatedBy            = result.EventId,
            CreationTimestamp    = now,
            LastUpdatedBy        = result.EventId,
            LastUpdateTimestamp  = now
        });

        return result.EmploymentId;
    }

    private HireEmployeeCommand BuildHireCommand(string tag)
        => new()
        {
            LegalFirstName       = "Gate",
            LegalLastName        = $"Test-{tag}",
            DateOfBirth          = new DateOnly(1990, 6, 1),
            NationalIdentifier   = "123-45-6789",
            PhonePrimary         = "555-000-0000",
            EmailPersonal        = "gate@test.example",
            AddressLine1         = "1 Test Lane",
            City                 = "Springfield",
            StateCode            = "IL",
            PostalCode           = "62701",
            CountryCode          = "US",
            LegalEntityId        = LegalEntityId,
            EmployeeNumber       = $"GATE-{tag}-{Guid.NewGuid():N}".Substring(0, 30),
            EmploymentTypeId     = _lookupCache.GetId(LookupTables.EmploymentType,      "EMPLOYEE"),
            EmploymentStartDate  = new DateOnly(2024, 1, 15),
            FlsaStatusId         = _lookupCache.GetId(LookupTables.FlsaStatus,          "NON_EXEMPT"),
            FullPartTimeStatusId = _lookupCache.GetId(LookupTables.FullPartTimeStatus,  "FULL_TIME"),
            JobId                = JobId,
            DepartmentId         = DepartmentId,
            LocationId           = LocationId,
            RateTypeId           = _lookupCache.GetId(LookupTables.CompensationRateType,"HOURLY"),
            BaseRate             = 25.00m,
            PayFrequencyId       = _lookupCache.GetId(LookupTables.PayFrequency,        "BIWEEKLY"),
            ChangeReasonCode     = "NEW_HIRE",
            InitiatedBy          = Guid.NewGuid()
        };

    private async Task<RunProgress> RunJobAsync(Guid runId, IPayrollHoursSource? hoursSource = null)
        => await DriveJobAsync(runId, hoursSource)
           ?? throw new InvalidOperationException("Job completed with no progress update.");

    // Drives the background job for one run through whatever status it is in, returning the last
    // progress update (null if the path emits none — e.g. the release path is a pure status flip).
    private async Task<RunProgress?> DriveJobAsync(Guid runId, IPayrollHoursSource? hoursSource = null)
    {
        var channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true
        });
        channel.Writer.TryWrite(runId);
        channel.Writer.Complete();

        var progress = new TestRunProgressNotifier();

        // Shared composition (TestSupport/PayrollRunTestContainer) registers every
        // dependency PayrollRunJob/CalculationEngine pull, including the cross-module
        // collaborators stubbed for a payroll-only test. See ToDo #36.
        await using var container = PayrollRunTestContainer.Build(_connectionFactory, _lookupCache, hoursSource);

        var job = new PayrollRunJob(
            channel,
            container,
            NullLogger<PayrollRunJob>.Instance,
            progress);

        await job.StartAsync(CancellationToken.None);
        await job.ExecuteTask!;
        await job.StopAsync(CancellationToken.None);

        return progress.Last;
    }

    // ---------------------------------------------------------------------------
    // IDisposable — clean up all DB rows created by this test instance
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        using var conn = _connectionFactory.CreateConnection();

        foreach (var runId in _runIds)
        {
            conn.Execute(
                """
                DELETE FROM accumulator_contribution WHERE source_run_id = @Id
                """, new { Id = runId });

            conn.Execute(
                """
                DELETE FROM accumulator_impact WHERE payroll_run_id = @Id
                """, new { Id = runId });

            conn.Execute(
                """
                DELETE FROM earnings_result_line
                WHERE employee_payroll_result_id IN (
                    SELECT employee_payroll_result_id FROM employee_payroll_result
                    WHERE payroll_run_id = @Id)
                """, new { Id = runId });

            conn.Execute(
                """
                DELETE FROM deduction_result_line
                WHERE employee_payroll_result_id IN (
                    SELECT employee_payroll_result_id FROM employee_payroll_result
                    WHERE payroll_run_id = @Id)
                """, new { Id = runId });

            conn.Execute(
                """
                DELETE FROM tax_result_line
                WHERE employee_payroll_result_id IN (
                    SELECT employee_payroll_result_id FROM employee_payroll_result
                    WHERE payroll_run_id = @Id)
                """, new { Id = runId });

            conn.Execute(
                """
                DELETE FROM employer_contribution_result_line
                WHERE employee_payroll_result_id IN (
                    SELECT employee_payroll_result_id FROM employee_payroll_result
                    WHERE payroll_run_id = @Id)
                """, new { Id = runId });
            conn.Execute("""
                DELETE FROM wage_base_result_line
                WHERE employee_payroll_result_id IN (
                    SELECT employee_payroll_result_id FROM employee_payroll_result
                    WHERE payroll_run_id = @Id)
                """, new { Id = runId });

            conn.Execute("DELETE FROM employee_payroll_result WHERE payroll_run_id = @Id",  new { Id = runId });
            conn.Execute("DELETE FROM payroll_run_result_set   WHERE payroll_run_id = @Id", new { Id = runId });
            conn.Execute("DELETE FROM payroll_run_exception    WHERE run_id          = @Id", new { Id = runId });
            conn.Execute("DELETE FROM payroll_run             WHERE run_id           = @Id", new { Id = runId });
        }

        foreach (var id in _employmentIds)
        {
            conn.Execute("DELETE FROM accumulator_reset_audit WHERE participant_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM accumulator_balance     WHERE participant_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM payroll_profile     WHERE employment_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM employee_event      WHERE employment_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM compensation_record WHERE employment_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM assignment          WHERE employment_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM employment          WHERE employment_id = @Id", new { Id = id });
        }

        foreach (var id in _personIds)
        {
            conn.Execute("DELETE FROM person_address WHERE person_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM person         WHERE person_id = @Id", new { Id = id });
        }
    }

    // ---------------------------------------------------------------------------
    // Inner types
    // ---------------------------------------------------------------------------

    private sealed class TestRunProgressNotifier : IRunProgressNotifier
    {
        public RunProgress? Last { get; private set; }

        public Task UpdateAsync(RunProgress progress)
        {
            Last = progress;
            return Task.CompletedTask;
        }

        public RunProgress? GetProgress(Guid runId)
            => Last?.RunId == runId ? Last : null;
    }
}
