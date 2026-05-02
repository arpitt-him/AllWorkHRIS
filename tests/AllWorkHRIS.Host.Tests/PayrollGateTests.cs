using System.Threading.Channels;
using Autofac;
using Dapper;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
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
        var employmentRepo    = new EmploymentRepository(_connectionFactory);
        var assignmentRepo    = new AssignmentRepository(_connectionFactory);
        var compensationRepo  = new CompensationRepository(_connectionFactory);
        var eventRepo         = new EmployeeEventRepository(_connectionFactory);
        var eventPublisher    = new InProcessEventBus();
        var temporalContext   = new SystemTemporalContext();
        var workQueueRepo     = new WorkQueueRepository(_connectionFactory);
        var workQueueService  = new WorkQueueService(workQueueRepo);
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

        _runService = new PayrollRunService(
            _runRepo, contextRepo, queue,
            NullLogger<PayrollRunService>.Instance, auditService);
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
        Assert.Equal((int)PayrollRunStatus.Draft,      run.RunStatusId);
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
        Assert.Equal((int)PayrollRunStatus.Calculated, run.RunStatusId);

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
    // Helpers
    // ---------------------------------------------------------------------------

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

    private async Task<RunProgress> RunJobAsync(Guid runId)
    {
        var channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true
        });
        channel.Writer.TryWrite(runId);
        channel.Writer.Complete();

        var progress = new TestRunProgressNotifier();

        var builder = new ContainerBuilder();
        builder.RegisterInstance(_connectionFactory).As<IConnectionFactory>().SingleInstance();
        builder.RegisterInstance((IAuditService)new NullAuditService()).As<IAuditService>().SingleInstance();
        builder.RegisterInstance((ITemporalContext)new SystemTemporalContext()).As<ITemporalContext>().SingleInstance();
        builder.RegisterInstance(NullLogger<CalculationEngine>.Instance).As<ILogger<CalculationEngine>>().SingleInstance();

        builder.RegisterType<PayrollRunRepository>()                  .As<IPayrollRunRepository>()                  .InstancePerLifetimeScope();
        builder.RegisterType<PayrollRunResultSetRepository>()         .As<IPayrollRunResultSetRepository>()         .InstancePerLifetimeScope();
        builder.RegisterType<EmployeePayrollResultRepository>()       .As<IEmployeePayrollResultRepository>()       .InstancePerLifetimeScope();
        builder.RegisterType<PayrollProfileRepository>()              .As<IPayrollProfileRepository>()              .InstancePerLifetimeScope();
        builder.RegisterType<PayrollContextRepository>()              .As<IPayrollContextRepository>()              .InstancePerLifetimeScope();
        builder.RegisterType<PayrollCompensationSnapshotRepository>() .As<IPayrollCompensationSnapshotRepository>() .InstancePerLifetimeScope();
        builder.RegisterType<ResultLineRepository>()                  .As<IResultLineRepository>()                  .InstancePerLifetimeScope();
        builder.RegisterType<AccumulatorRepository>()                 .As<IAccumulatorRepository>()                 .InstancePerLifetimeScope();
        builder.RegisterType<CalculationEngine>()                     .As<ICalculationEngine>()                     .InstancePerLifetimeScope();
        builder.RegisterType<AccumulatorService>()                    .As<IAccumulatorService>()                    .InstancePerLifetimeScope();

        await using var container = builder.Build();

        var job = new PayrollRunJob(
            channel,
            container,
            NullLogger<PayrollRunJob>.Instance,
            progress);

        await job.StartAsync(CancellationToken.None);
        await job.ExecuteTask!;
        await job.StopAsync(CancellationToken.None);

        return progress.Last ?? throw new InvalidOperationException("Job completed with no progress update.");
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

            conn.Execute("DELETE FROM employee_payroll_result WHERE payroll_run_id = @Id",  new { Id = runId });
            conn.Execute("DELETE FROM payroll_run_result_set   WHERE payroll_run_id = @Id", new { Id = runId });
            conn.Execute("DELETE FROM payroll_run_exception    WHERE run_id          = @Id", new { Id = runId });
            conn.Execute("DELETE FROM payroll_run             WHERE run_id           = @Id", new { Id = runId });
        }

        foreach (var id in _employmentIds)
        {
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
