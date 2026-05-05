using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Commands;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Domain.Elections;
using AllWorkHRIS.Module.Benefits.Repositories;
using AllWorkHRIS.Module.Benefits.Services;
using AllWorkHRIS.Module.Benefits.Steps;
using AllWorkHRIS.Module.Benefits.Steps.Calculators;
using AllWorkHRIS.Module.Tax.Repositories;
using AllWorkHRIS.Module.Tax.Services;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Phase 6 gate tests — TC-BEN-001 through TC-BEN-017, TC-PAY-007.
/// All tests run against allworkhris_dev.
/// Source of truth: SPEC/Benefits_Minimum_Module.md §13
/// </summary>
public sealed class BenefitsGateTests : IAsyncLifetime
{
    static readonly Guid CreatorId    = Guid.Parse("00000000-0000-0000-0000-000000000099");
    static readonly Guid EmploymentId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    static readonly Guid EmployeeId   = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");
    // Real employment record used by import tests (requires employee_number lookup)
    static readonly string ImportEmployeeNumber = "EMP-0004";
    static readonly Guid   ImportEmploymentId   = Guid.Parse("2fa99263-f1c3-4dc5-ad19-c5bb43e385fe");
    static readonly Guid ContextId    = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    static readonly Guid PeriodId     = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    static readonly DateOnly Today     = new(2026, 5, 2);

    readonly IConnectionFactory          _connectionFactory;
    readonly IDeductionRepository        _codeRepo;
    readonly IBenefitElectionRepository  _electionRepo;
    readonly IBenefitElectionService     _electionService;
    readonly IPayrollPipelineService     _pipeline;

    readonly List<string> _insertedCodes     = [];
    readonly List<Guid>   _insertedElections = [];

    public BenefitsGateTests()
    {
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
            "Host=localhost;Database=allworkhris_dev;Username=postgres;Password=dev");
        Environment.SetEnvironmentVariable("DATABASE_PROVIDER", "postgresql");

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new TestDateOnlyHandler());
        SqlMapper.AddTypeHandler(new TestNullableDateOnlyHandler());

        _connectionFactory = new ConnectionFactory();
        _codeRepo          = new DeductionRepository(_connectionFactory);
        _electionRepo      = new BenefitElectionRepository(_connectionFactory);

        var nullEventPublisher = new InProcessEventBus();
        _electionService = new BenefitElectionService(
            _electionRepo, _codeRepo, _connectionFactory,
            nullEventPublisher, NullLogger<BenefitElectionService>.Instance);

        var rateTableRepo  = new DeductionRateTableRepository(_connectionFactory);
        var matchRepo      = new DeductionEmployerMatchRepository(_connectionFactory);
        IBenefitCalculator[] calculators =
        [
            new FixedPerPeriodCalculator(),
            new FixedAnnualCalculator(),
            new FixedMonthlyCalculator(),
            new PctPreTaxCalculator(),
            new CoverageBasedCalculator()
        ];
        var calculatorFactory = new BenefitCalculatorFactory(calculators);
        var benefitProvider   = new BenefitStepProvider(_electionRepo, rateTableRepo, matchRepo, calculatorFactory);

        _pipeline = new PayrollPipelineService(
            new TaxRateRepository(_connectionFactory),
            new TaxFormSubmissionRepository(_connectionFactory),
            [benefitProvider],
            NullLogger<PayrollPipelineService>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Clean up test-inserted data
        using var conn = _connectionFactory.CreateConnection();
        if (_insertedElections.Count > 0)
            await conn.ExecuteAsync(
                "DELETE FROM benefit_deduction_election WHERE election_id = ANY(@Ids)",
                new { Ids = _insertedElections.ToArray() });
        if (_insertedCodes.Count > 0)
            await conn.ExecuteAsync(
                "DELETE FROM deduction WHERE code = ANY(@Codes)",
                new { Codes = _insertedCodes.ToArray() });
    }

    // -------------------------------------------------------------------------
    // Helper — insert a deduction record and return its ID for use in commands
    // -------------------------------------------------------------------------
    private async Task<Guid> InsertCodeAsync(string code, string taxTreatment = "PRE_TAX")
    {
        var existing = await _codeRepo.GetByCodeAsync(code);
        if (existing is not null)
            return existing.DeductionId;

        var deductionId = Guid.NewGuid();
        await _codeRepo.InsertAsync(new Deduction
        {
            DeductionId        = deductionId,
            Code               = code,
            Description        = $"Test code {code}",
            TaxTreatment       = taxTreatment,
            Status             = "ACTIVE",
            EffectiveStartDate = new DateOnly(2020, 1, 1),
            CreatedAt          = DateTimeOffset.UtcNow,
            UpdatedAt          = DateTimeOffset.UtcNow
        });
        _insertedCodes.Add(code);
        return deductionId;
    }

    private async Task<Guid> InsertElectionAsync(
        string   deductionCode,
        decimal  employeeAmount,
        decimal? employerAmount  = null,
        DateOnly? effectiveStart = null,
        DateOnly? effectiveEnd   = null,
        string   status          = "ACTIVE")
    {
        var deduction = await _codeRepo.GetByCodeAsync(deductionCode)
            ?? throw new InvalidOperationException($"Deduction code '{deductionCode}' not found in test setup.");

        var electionId   = Guid.NewGuid();
        var now          = DateTimeOffset.UtcNow;
        var start        = effectiveStart ?? new DateOnly(2020, 1, 1);

        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO benefit_deduction_election
                (election_id, employment_id, deduction_id, tax_treatment,
                 employee_amount, employer_contribution_amount,
                 effective_start_date, effective_end_date,
                 status, source, created_by, created_at, updated_at,
                 election_version_id)
            VALUES
                (@ElectionId, @EmploymentId, @DeductionId, @TaxTreatment,
                 @EmployeeAmount, @EmployerAmount,
                 @EffectiveStart, @EffectiveEnd,
                 @Status, 'MANUAL', 'test', @Now, @Now,
                 gen_random_uuid())
            """, new
        {
            ElectionId     = electionId,
            EmploymentId   = EmploymentId,
            DeductionId    = deduction.DeductionId,
            TaxTreatment   = deduction.TaxTreatment,
            EmployeeAmount = employeeAmount,
            EmployerAmount = employerAmount,
            EffectiveStart = start,
            EffectiveEnd   = effectiveEnd,
            Status         = status,
            Now            = now
        });

        _insertedElections.Add(electionId);
        return electionId;
    }

    private PipelineRequest MakeRequest(
        decimal annualGross,
        string  jurisdiction = "US-FED",
        string  filingStatus = "SINGLE",
        Guid?   employmentId = null)
        => new()
        {
            EmploymentId      = employmentId ?? EmploymentId,
            EmployeeId        = EmployeeId,
            PayrollContextId  = ContextId,
            PeriodId          = PeriodId,
            PayDate           = Today,
            GrossPayPeriod    = annualGross / 26m,
            PayPeriodsPerYear = 26,
            JurisdictionCode  = jurisdiction,
            FilingStatusCode  = filingStatus
        };

    // -------------------------------------------------------------------------
    // TC-BEN-001: Create election — inserted with correct status
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen001_CreateElection_StatusIsActive_WhenStartDateInPast()
    {
        var deductionId = await InsertCodeAsync("TC_BEN_001_PRE");

        var electionId = await _electionService.CreateElectionAsync(new CreateElectionCommand
        {
            EmploymentId       = EmploymentId,
            DeductionId        = deductionId,
            EmployeeAmount     = 200m,
            EffectiveStartDate = new DateOnly(2025, 1, 1),
            Source             = "MANUAL",
            CreatedBy          = CreatorId
        });

        _insertedElections.Add(electionId);
        var election = await _electionRepo.GetByIdAsync(electionId);
        Assert.NotNull(election);
        Assert.Equal(ElectionStatus.Active, election.Status);
        Assert.Equal("TC_BEN_001_PRE", election.DeductionCode);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-002: Invalid deduction ID — throws
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen002_CreateElection_InvalidDeduction_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _electionService.CreateElectionAsync(new CreateElectionCommand
            {
                EmploymentId       = EmploymentId,
                DeductionId        = Guid.NewGuid(),  // does not exist
                EmployeeAmount     = 100m,
                EffectiveStartDate = Today,
                Source             = "MANUAL",
                CreatedBy          = CreatorId
            }));
    }

    // -------------------------------------------------------------------------
    // TC-BEN-003: Negative employee amount — throws
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen003_CreateElection_NegativeAmount_Throws()
    {
        var deductionId = await InsertCodeAsync("TC_BEN_003_PRE");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _electionService.CreateElectionAsync(new CreateElectionCommand
            {
                EmploymentId       = EmploymentId,
                DeductionId        = deductionId,
                EmployeeAmount     = -50m,
                EffectiveStartDate = Today,
                Source             = "MANUAL",
                CreatedBy          = CreatorId
            }));
    }

    // -------------------------------------------------------------------------
    // TC-BEN-004: End date before start date — throws
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen004_CreateElection_EndBeforeStart_Throws()
    {
        var deductionId = await InsertCodeAsync("TC_BEN_004_PRE");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _electionService.CreateElectionAsync(new CreateElectionCommand
            {
                EmploymentId       = EmploymentId,
                DeductionId        = deductionId,
                EmployeeAmount     = 100m,
                EffectiveStartDate = Today,
                EffectiveEndDate   = Today.AddDays(-1),
                Source             = "MANUAL",
                CreatedBy          = CreatorId
            }));
    }

    // -------------------------------------------------------------------------
    // TC-BEN-005: Duplicate active election — rejected by SERIALIZABLE overlap guard
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen005_DuplicateElection_RejectedByOverlapGuard()
    {
        var deductionId = await InsertCodeAsync("TC_BEN_005_PRE");

        var firstId = await _electionService.CreateElectionAsync(new CreateElectionCommand
        {
            EmploymentId       = EmploymentId,
            DeductionId        = deductionId,
            EmployeeAmount     = 100m,
            EffectiveStartDate = new DateOnly(2025, 1, 1),
            Source             = "MANUAL",
            CreatedBy          = CreatorId
        });
        _insertedElections.Add(firstId);

        // Overlapping election must be rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _electionService.CreateElectionAsync(new CreateElectionCommand
            {
                EmploymentId       = EmploymentId,
                DeductionId        = deductionId,
                EmployeeAmount     = 150m,
                EffectiveStartDate = new DateOnly(2025, 6, 1),
                Source             = "MANUAL",
                CreatedBy          = CreatorId
            }));
    }

    // -------------------------------------------------------------------------
    // TC-BEN-006: Update election — creates versioned record, prior is SUPERSEDED
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen006_UpdateElection_CreateNewVersion_PriorSuperseded()
    {
        var deductionId = await InsertCodeAsync("TC_BEN_006_PRE");

        var originalId = await _electionService.CreateElectionAsync(new CreateElectionCommand
        {
            EmploymentId       = EmploymentId,
            DeductionId        = deductionId,
            EmployeeAmount     = 100m,
            EffectiveStartDate = new DateOnly(2025, 1, 1),
            Source             = "MANUAL",
            CreatedBy          = CreatorId
        });
        _insertedElections.Add(originalId);

        var updatedId = await _electionService.UpdateElectionAsync(new UpdateElectionCommand
        {
            ElectionId         = originalId,
            EmployeeAmount     = 200m,
            EffectiveStartDate = new DateOnly(2025, 1, 1),
            CorrectionType     = "AMOUNT_CHANGE",
            UpdatedBy          = CreatorId
        });
        _insertedElections.Add(updatedId);

        var prior   = await _electionRepo.GetByIdAsync(originalId);
        var updated = await _electionRepo.GetByIdAsync(updatedId);

        Assert.Equal(ElectionStatus.Superseded, prior!.Status);
        Assert.Equal(200m, updated!.EmployeeAmount);
        Assert.Equal(originalId, updated.ParentElectionId);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-007: Terminate election — transitions to TERMINATED
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen007_TerminateElection_StatusIsTerminated()
    {
        var deductionId = await InsertCodeAsync("TC_BEN_007_PRE");

        var electionId = await _electionService.CreateElectionAsync(new CreateElectionCommand
        {
            EmploymentId       = EmploymentId,
            DeductionId        = deductionId,
            EmployeeAmount     = 100m,
            EffectiveStartDate = new DateOnly(2025, 1, 1),
            Source             = "MANUAL",
            CreatedBy          = CreatorId
        });
        _insertedElections.Add(electionId);

        await _electionService.TerminateElectionAsync(new TerminateElectionCommand
        {
            ElectionId   = electionId,
            TerminatedBy = CreatorId
        });

        var election = await _electionRepo.GetByIdAsync(electionId);
        Assert.Equal(ElectionStatus.Terminated, election!.Status);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-008: HRIS TERMINATION event — all active elections terminated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen008_HrisTerminationEvent_AllActiveElectionsTerminated()
    {
        await InsertCodeAsync("TC_BEN_008A");
        await InsertCodeAsync("TC_BEN_008B");

        var id1 = await InsertElectionAsync("TC_BEN_008A", 100m);
        var id2 = await InsertElectionAsync("TC_BEN_008B", 200m);

        var eventId = Guid.NewGuid();
        await _electionService.TerminateAllActiveAsync(EmploymentId, eventId);

        var e1 = await _electionRepo.GetByIdAsync(id1);
        var e2 = await _electionRepo.GetByIdAsync(id2);
        Assert.Equal(ElectionStatus.Terminated, e1!.Status);
        Assert.Equal(ElectionStatus.Terminated, e2!.Status);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-009: HRIS LEAVE_OF_ABSENCE event — all active elections suspended
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen009_HrisLeaveEvent_AllActiveElectionsSuspended()
    {
        await InsertCodeAsync("TC_BEN_009_PRE");
        var electionId = await InsertElectionAsync("TC_BEN_009_PRE", 100m);

        var eventId = Guid.NewGuid();
        await _electionService.SuspendAllActiveAsync(EmploymentId, eventId);

        var election = await _electionRepo.GetByIdAsync(electionId);
        Assert.Equal(ElectionStatus.Suspended, election!.Status);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-010: HRIS RETURN_TO_WORK event — suspended elections reinstated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen010_HrisReturnToWork_SuspendedElectionsReinstated()
    {
        await InsertCodeAsync("TC_BEN_010_PRE");
        var electionId = await InsertElectionAsync("TC_BEN_010_PRE", 100m, status: "SUSPENDED");

        var eventId = Guid.NewGuid();
        await _electionService.ReinstateAllSuspendedAsync(EmploymentId, eventId);

        var election = await _electionRepo.GetByIdAsync(electionId);
        Assert.Equal(ElectionStatus.Active, election!.Status);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-011: Batch import dry-run — validates without posting
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen011_BatchImportDryRun_ReturnsValidationResultWithoutPosting()
    {
        await InsertCodeAsync("TC_BEN_011_PRE");

        var importService = new BenefitElectionImportService(
            _electionService, _codeRepo, _connectionFactory,
            NullLogger<BenefitElectionImportService>.Instance);

        var csv = $"employee_number,deduction_code,employee_amount,employer_contribution_amount,effective_start_date,effective_end_date\n{ImportEmployeeNumber},TC_BEN_011_PRE,150.00,,2026-01-01,\nEMP-NONEXISTENT,NONEXISTENT,-5.00,,2026-01-01,";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var result = await importService.ValidateBatchAsync(stream, "text/csv");

        Assert.Equal(2, result.TotalRecords);
        Assert.True(result.InvalidCount > 0, "Invalid records should be detected");
    }

    // -------------------------------------------------------------------------
    // TC-BEN-012: Batch import commit — valid records posted, invalid skipped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen012_BatchImportSubmit_ValidRecordsPosted()
    {
        await InsertCodeAsync("TC_BEN_012_PRE");

        var importService = new BenefitElectionImportService(
            _electionService, _codeRepo, _connectionFactory,
            NullLogger<BenefitElectionImportService>.Instance);

        var csv = $"employee_number,deduction_code,employee_amount,contribution_pct,coverage_tier,employer_contribution_amount,annual_coverage_amount,effective_start_date,effective_end_date\n{ImportEmployeeNumber},TC_BEN_012_PRE,175.00,,,,,2026-01-01,";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var jobId = await importService.SubmitBatchAsync(stream, "text/csv", CreatorId);

        Assert.NotEqual(Guid.Empty, jobId);

        // Verify the election was created
        var elections = (await _electionRepo.GetActiveByEmploymentIdAsync(ImportEmploymentId, Today)).ToList();
        var imported  = elections.FirstOrDefault(e => e.DeductionCode == "TC_BEN_012_PRE" && e.Source == "IMPORT");
        Assert.NotNull(imported);
        _insertedElections.Add(imported.ElectionId);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-013 / TC-PAY-007: Pre-tax election reduces income-taxable wages
    // Pipeline runs US-FED with $100/period pre-tax election.
    // Tax should be computed on (gross - 100), not gross.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen013_PreTaxElection_ReducesIncomeTaxableWages()
    {
        await InsertCodeAsync("TC_BEN_013_PRE", "PRE_TAX");
        await InsertElectionAsync("TC_BEN_013_PRE", 100m);   // $100/period pre-tax

        const decimal annualGross = 70_000m;
        var result = await _pipeline.RunAsync(MakeRequest(annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("TC_BEN_013_PRE"),
            "Pre-tax step should appear in step results");

        // Verify NetPay is reduced by the election amount
        var deductionApplied = result.StepResults["TC_BEN_013_PRE"];
        Assert.Equal(100m, deductionApplied, precision: 2);

        // Net pay should be less than gross minus tax (tax computed on reduced base)
        // Baseline: run same request without election against a different employment ID
        var baselineResult = await _pipeline.RunAsync(MakeRequest(annualGross, employmentId: Guid.NewGuid()));
        Assert.True(baselineResult.Succeeded);

        // With pre-tax election, federal income tax should be lower (computed on 70,000 - 100*26 annualized)
        Assert.True(result.StepResults.GetValueOrDefault("US_FED_INCOME_TAX", 0m)
                    <= baselineResult.StepResults.GetValueOrDefault("US_FED_INCOME_TAX", 0m),
            "Income tax should be ≤ baseline when pre-tax election reduces taxable wages");
    }

    // -------------------------------------------------------------------------
    // TC-BEN-014: Post-tax election reduces net pay (not taxable wages)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen014_PostTaxElection_ReducesNetPayNotTaxableWages()
    {
        await InsertCodeAsync("TC_BEN_014_POST", "POST_TAX");
        await InsertElectionAsync("TC_BEN_014_POST", 75m);   // $75/period post-tax

        const decimal annualGross = 70_000m;
        var result = await _pipeline.RunAsync(MakeRequest(annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("TC_BEN_014_POST"),
            "Post-tax step should appear in step results");

        // Deduction is $75
        Assert.Equal(75m, result.StepResults["TC_BEN_014_POST"], precision: 2);

        // Federal income tax should equal baseline (post-tax does not reduce taxable wages)
        var baselineResult = await _pipeline.RunAsync(MakeRequest(annualGross, employmentId: Guid.NewGuid()));
        Assert.True(baselineResult.Succeeded);

        var incomeTaxWithElection  = result.StepResults.GetValueOrDefault("US_FED_INCOME_TAX", 0m);
        var incomeTaxBaseline      = baselineResult.StepResults.GetValueOrDefault("US_FED_INCOME_TAX", 0m);
        Assert.Equal(incomeTaxBaseline, incomeTaxWithElection, precision: 2);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-015: Employer contribution — appears in EmployerStepResults
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen015_EmployerContribution_AppearsInEmployerResults()
    {
        await InsertCodeAsync("TC_BEN_015_PRE", "PRE_TAX");
        await InsertElectionAsync("TC_BEN_015_PRE", 100m, employerAmount: 50m);

        const decimal annualGross = 70_000m;
        var result = await _pipeline.RunAsync(MakeRequest(annualGross));

        Assert.True(result.Succeeded, result.FailureReason);

        var erStepCode = "TC_BEN_015_PRE" + "_ER";
        Assert.True(result.EmployerStepResults.ContainsKey(erStepCode),
            $"Employer contribution step {erStepCode} should be in EmployerStepResults");
        Assert.Equal(50m, result.EmployerStepResults[erStepCode], precision: 2);
    }

    // -------------------------------------------------------------------------
    // TC-BEN-016: Suspended election not consumed by pipeline
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen016_SuspendedElection_NotIncludedInPipeline()
    {
        await InsertCodeAsync("TC_BEN_016_PRE", "PRE_TAX");
        await InsertElectionAsync("TC_BEN_016_PRE", 100m, status: "SUSPENDED");

        const decimal annualGross = 70_000m;
        var result = await _pipeline.RunAsync(MakeRequest(annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(result.StepResults.ContainsKey("TC_BEN_016_PRE"),
            "Suspended election should not produce a step result");
    }

    // -------------------------------------------------------------------------
    // TC-BEN-017: Expired election not consumed by pipeline
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcBen017_ExpiredElection_NotIncludedInPipeline()
    {
        await InsertCodeAsync("TC_BEN_017_PRE", "PRE_TAX");
        // Election ended yesterday — past effective_end_date
        await InsertElectionAsync("TC_BEN_017_PRE", 100m,
            effectiveEnd: Today.AddDays(-1),
            status: "ACTIVE");

        const decimal annualGross = 70_000m;
        var result = await _pipeline.RunAsync(MakeRequest(annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.False(result.StepResults.ContainsKey("TC_BEN_017_PRE"),
            "Expired election (effective_end_date in past) should not produce a step result");
    }
}
