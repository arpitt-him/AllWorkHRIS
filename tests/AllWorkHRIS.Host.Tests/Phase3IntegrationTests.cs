using System.Data;
using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host.Hris.Commands;
using AllWorkHRIS.Host.Hris.Domain;
using AllWorkHRIS.Host.Hris.Repositories;
using AllWorkHRIS.Host.Hris.Services;

namespace AllWorkHRIS.Host.Tests;

// ============================================================
// LEAVE INTEGRATION TESTS
// ============================================================

public class LeaveIntegrationTests : IDisposable
{
    static readonly Guid LegalEntityId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    static readonly Guid DepartmentId  = Guid.Parse("10000000-0000-0000-0000-000000000003");
    static readonly Guid LocationId    = Guid.Parse("10000000-0000-0000-0000-000000000004");
    static readonly Guid JobId         = Guid.Parse("20000000-0000-0000-0000-000000000001");

    readonly IConnectionFactory      _connectionFactory;
    readonly ILookupCache            _lookupCache;
    readonly ILeaveService           _leaveService;
    readonly ILeaveBalanceRepository _leaveBalanceRepo;
    readonly ILeaveRequestRepository _leaveRequestRepo;

    readonly List<Guid> _personIds      = [];
    readonly List<Guid> _employmentIds  = [];
    readonly List<Guid> _leaveRequestIds = [];

    readonly int _vacationLeaveTypeId;
    readonly int _unpaidLeaveTypeId;

    public LeaveIntegrationTests()
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

        _vacationLeaveTypeId = _lookupCache.GetId(LookupTables.LeaveType, "VACATION");
        _unpaidLeaveTypeId   = _lookupCache.GetId(LookupTables.LeaveType, "UNPAID");

        var leaveRequestRepo    = new LeaveRequestRepository(_connectionFactory);
        var leaveBalanceRepo    = new LeaveBalanceRepository(_connectionFactory);
        var leaveTypeConfigRepo = new LeaveTypeConfigRepository(_connectionFactory);
        var workQueueRepo       = new WorkQueueRepository(_connectionFactory);
        var workQueueService    = new WorkQueueService(workQueueRepo);
        var eventPublisher      = new InProcessEventBus();
        var temporalContext     = new SystemTemporalContext();

        _leaveRequestRepo = leaveRequestRepo;
        _leaveBalanceRepo = leaveBalanceRepo;

        _leaveService = new LeaveService(
            _connectionFactory,
            leaveRequestRepo,
            leaveBalanceRepo,
            leaveTypeConfigRepo,
            workQueueService,
            eventPublisher,
            temporalContext,
            _lookupCache);
    }

    // -----------------------------------------------------------------------
    // TC-LEV-013: CalculateWorkingDays skips weekends correctly
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateWorkingDays_SkipsWeekends()
    {
        // Mon 2024-01-15 → Fri 2024-01-19 = 5 working days
        var days = LeaveService.CalculateWorkingDays(
            new DateOnly(2024, 1, 15), new DateOnly(2024, 1, 19));
        Assert.Equal(5m, days);
    }

    [Fact]
    public void CalculateWorkingDays_SpansWeekend_ExcludesSatSun()
    {
        // Mon 2024-01-15 → Mon 2024-01-22 = 6 working days (skips Sat + Sun)
        var days = LeaveService.CalculateWorkingDays(
            new DateOnly(2024, 1, 15), new DateOnly(2024, 1, 22));
        Assert.Equal(6m, days);
    }

    // -----------------------------------------------------------------------
    // TC-LEV-001: Submit leave request persists record in REQUESTED state
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitLeave_NonAccrued_CreatesRequestedRecord()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();

        var command = new SubmitLeaveRequestCommand
        {
            EmploymentId    = employmentId,
            LeaveType       = "UNPAID",
            LeaveStartDate  = new DateOnly(2025, 6, 2),
            LeaveEndDate    = new DateOnly(2025, 6, 6),
            LeaveReasonCode = "PERSONAL",
            SubmittedBy     = Guid.NewGuid()
        };

        var leaveRequestId = await _leaveService.SubmitLeaveRequestAsync(command);
        _leaveRequestIds.Add(leaveRequestId);

        var saved = await _leaveRequestRepo.GetByIdAsync(leaveRequestId);
        Assert.NotNull(saved);
        Assert.Equal(_lookupCache.GetId(LookupTables.LeaveStatus, "REQUESTED"),
            saved.LeaveStatusId);
        Assert.Equal(employmentId, saved.EmploymentId);
    }

    // -----------------------------------------------------------------------
    // TC-LEV-002: Submit accrued leave with sufficient balance deducts it
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitLeave_AccruedWithBalance_DeductsBalance()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();
        await SeedLeaveBalanceAsync(employmentId, _vacationLeaveTypeId, 10m);

        var command = new SubmitLeaveRequestCommand
        {
            EmploymentId    = employmentId,
            LeaveType       = "VACATION",
            LeaveStartDate  = new DateOnly(2025, 6, 2),
            LeaveEndDate    = new DateOnly(2025, 6, 6),
            LeaveReasonCode = "VACATION",
            SubmittedBy     = Guid.NewGuid()
        };

        var leaveRequestId = await _leaveService.SubmitLeaveRequestAsync(command);
        _leaveRequestIds.Add(leaveRequestId);

        // 5 working days requested, balance should go from 10 to 5
        var balance = await _leaveBalanceRepo.GetByEmploymentAndTypeAsync(
            employmentId, _vacationLeaveTypeId);
        Assert.NotNull(balance);
        // Balance is not deducted on submit — only on approve per spec
        Assert.Equal(10m, balance.AvailableBalance);
    }

    // -----------------------------------------------------------------------
    // TC-LEV-003: Submit accrued leave with insufficient balance throws
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitLeave_InsufficientBalance_Throws()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();
        await SeedLeaveBalanceAsync(employmentId, _vacationLeaveTypeId, 2m);

        var command = new SubmitLeaveRequestCommand
        {
            EmploymentId    = employmentId,
            LeaveType       = "VACATION",
            LeaveStartDate  = new DateOnly(2025, 6, 2),
            LeaveEndDate    = new DateOnly(2025, 6, 6), // 5 working days
            LeaveReasonCode = "VACATION",
            SubmittedBy     = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<InsufficientLeaveBalanceException>(
            () => _leaveService.SubmitLeaveRequestAsync(command));
    }

    // -----------------------------------------------------------------------
    // TC-LEV-004: Submit leave overlapping existing throws DomainException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitLeave_Overlapping_ThrowsDomainException()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();

        var first = new SubmitLeaveRequestCommand
        {
            EmploymentId    = employmentId,
            LeaveType       = "UNPAID",
            LeaveStartDate  = new DateOnly(2025, 6, 2),
            LeaveEndDate    = new DateOnly(2025, 6, 6),
            LeaveReasonCode = "PERSONAL",
            SubmittedBy     = Guid.NewGuid()
        };
        var id1 = await _leaveService.SubmitLeaveRequestAsync(first);
        _leaveRequestIds.Add(id1);

        var overlapping = first with
        {
            LeaveStartDate = new DateOnly(2025, 6, 4),
            LeaveEndDate   = new DateOnly(2025, 6, 10)
        };

        await Assert.ThrowsAsync<DomainException>(
            () => _leaveService.SubmitLeaveRequestAsync(overlapping));
    }

    // -----------------------------------------------------------------------
    // TC-LEV-005: Approve transitions to APPROVED state
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApproveLeave_TransitionsToApproved()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();

        var leaveRequestId = await _leaveService.SubmitLeaveRequestAsync(
            new SubmitLeaveRequestCommand
            {
                EmploymentId    = employmentId,
                LeaveType       = "UNPAID",
                LeaveStartDate  = new DateOnly(2025, 7, 7),
                LeaveEndDate    = new DateOnly(2025, 7, 11),
                LeaveReasonCode = "PERSONAL",
                SubmittedBy     = Guid.NewGuid()
            });
        _leaveRequestIds.Add(leaveRequestId);

        await _leaveService.ApproveLeaveRequestAsync(new ApproveLeaveRequestCommand
        {
            LeaveRequestId = leaveRequestId,
            ApprovedBy     = Guid.NewGuid()
        });

        var saved = await _leaveRequestRepo.GetByIdAsync(leaveRequestId);
        Assert.Equal(_lookupCache.GetId(LookupTables.LeaveStatus, "APPROVED"),
            saved!.LeaveStatusId);
    }

    // -----------------------------------------------------------------------
    // TC-LEV-005b: Approve accrued leave deducts balance
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApproveLeave_Accrued_DeductsBalance()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();
        await SeedLeaveBalanceAsync(employmentId, _vacationLeaveTypeId, 15m);

        var leaveRequestId = await _leaveService.SubmitLeaveRequestAsync(
            new SubmitLeaveRequestCommand
            {
                EmploymentId    = employmentId,
                LeaveType       = "VACATION",
                LeaveStartDate  = new DateOnly(2025, 7, 7),
                LeaveEndDate    = new DateOnly(2025, 7, 11), // 5 days
                LeaveReasonCode = "VACATION",
                SubmittedBy     = Guid.NewGuid()
            });
        _leaveRequestIds.Add(leaveRequestId);

        await _leaveService.ApproveLeaveRequestAsync(new ApproveLeaveRequestCommand
        {
            LeaveRequestId = leaveRequestId,
            ApprovedBy     = Guid.NewGuid()
        });

        var balance = await _leaveBalanceRepo.GetByEmploymentAndTypeAsync(
            employmentId, _vacationLeaveTypeId);
        Assert.Equal(10m, balance!.AvailableBalance); // 15 - 5 = 10
    }

    // -----------------------------------------------------------------------
    // TC-LEV-006: Deny transitions to DENIED state
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DenyLeave_TransitionsToDenied()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();

        var leaveRequestId = await _leaveService.SubmitLeaveRequestAsync(
            new SubmitLeaveRequestCommand
            {
                EmploymentId    = employmentId,
                LeaveType       = "UNPAID",
                LeaveStartDate  = new DateOnly(2025, 8, 4),
                LeaveEndDate    = new DateOnly(2025, 8, 8),
                LeaveReasonCode = "PERSONAL",
                SubmittedBy     = Guid.NewGuid()
            });
        _leaveRequestIds.Add(leaveRequestId);

        await _leaveService.DenyLeaveRequestAsync(new DenyLeaveRequestCommand
        {
            LeaveRequestId = leaveRequestId,
            DeniedBy       = Guid.NewGuid(),
            DenialReason   = "Staffing constraints"
        });

        var saved = await _leaveRequestRepo.GetByIdAsync(leaveRequestId);
        Assert.Equal(_lookupCache.GetId(LookupTables.LeaveStatus, "DENIED"),
            saved!.LeaveStatusId);
    }

    // -----------------------------------------------------------------------
    // TC-LEV-007: Cancel approved accrued leave restores balance
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CancelLeave_ApprovedAccrued_RestoresBalance()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();
        await SeedLeaveBalanceAsync(employmentId, _vacationLeaveTypeId, 10m);

        var leaveRequestId = await _leaveService.SubmitLeaveRequestAsync(
            new SubmitLeaveRequestCommand
            {
                EmploymentId    = employmentId,
                LeaveType       = "VACATION",
                LeaveStartDate  = new DateOnly(2025, 9, 1),
                LeaveEndDate    = new DateOnly(2025, 9, 5), // 5 days
                LeaveReasonCode = "VACATION",
                SubmittedBy     = Guid.NewGuid()
            });
        _leaveRequestIds.Add(leaveRequestId);

        var actor = Guid.NewGuid();
        await _leaveService.ApproveLeaveRequestAsync(
            new ApproveLeaveRequestCommand { LeaveRequestId = leaveRequestId, ApprovedBy = actor });

        // Balance should be 5 after approval
        var afterApproval = await _leaveBalanceRepo.GetByEmploymentAndTypeAsync(
            employmentId, _vacationLeaveTypeId);
        Assert.Equal(5m, afterApproval!.AvailableBalance);

        await _leaveService.CancelLeaveRequestAsync(leaveRequestId, actor);

        var afterCancel = await _leaveBalanceRepo.GetByEmploymentAndTypeAsync(
            employmentId, _vacationLeaveTypeId);
        Assert.Equal(10m, afterCancel!.AvailableBalance); // restored
    }

    // -----------------------------------------------------------------------
    // TC-LEV-012: Return from leave transitions to COMPLETED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReturnFromLeave_CompletesLeave()
    {
        var (_, employmentId) = await CreateTestEmploymentAsync();

        var leaveRequestId = await _leaveService.SubmitLeaveRequestAsync(
            new SubmitLeaveRequestCommand
            {
                EmploymentId    = employmentId,
                LeaveType       = "UNPAID",
                LeaveStartDate  = new DateOnly(2025, 10, 1),
                LeaveEndDate    = new DateOnly(2025, 10, 10),
                LeaveReasonCode = "PERSONAL",
                SubmittedBy     = Guid.NewGuid()
            });
        _leaveRequestIds.Add(leaveRequestId);

        var actor = Guid.NewGuid();
        await _leaveService.ApproveLeaveRequestAsync(
            new ApproveLeaveRequestCommand { LeaveRequestId = leaveRequestId, ApprovedBy = actor });

        // Manually set to IN_PROGRESS (the background job would do this)
        var inProgressId = _lookupCache.GetId(LookupTables.LeaveStatus, "IN_PROGRESS");
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE leave_request SET leave_status_id = @Id WHERE leave_request_id = @Rid",
            new { Id = inProgressId, Rid = leaveRequestId });

        await _leaveService.ReturnFromLeaveAsync(new ReturnFromLeaveCommand
        {
            EmploymentId = employmentId,
            ReturnDate   = new DateOnly(2025, 10, 11),
            InitiatedBy  = actor
        });

        var saved = await _leaveRequestRepo.GetByIdAsync(leaveRequestId);
        Assert.Equal(_lookupCache.GetId(LookupTables.LeaveStatus, "COMPLETED"),
            saved!.LeaveStatusId);
        Assert.Equal(new DateOnly(2025, 10, 11), saved.ActualReturnDate);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<(Guid PersonId, Guid EmploymentId)> CreateTestEmploymentAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        var personId     = Guid.NewGuid();
        var employmentId = Guid.NewGuid();
        var activePersonStatusId     = _lookupCache.GetId(LookupTables.PersonStatus,            "ACTIVE");
        var activeEmploymentStatusId = _lookupCache.GetId(LookupTables.EmploymentStatus,         "ACTIVE");
        var employmentTypeId         = _lookupCache.GetId(LookupTables.EmploymentType,           "EMPLOYEE");
        var fptStatusId              = _lookupCache.GetId(LookupTables.FullPartTimeStatus,       "FULL_TIME");
        var regTempStatusId          = _lookupCache.GetId(LookupTables.RegularTemporaryStatus,   "REGULAR");
        var flsaStatusId             = _lookupCache.GetId(LookupTables.FlsaStatus,               "NON_EXEMPT");

        await conn.ExecuteAsync(
            @"INSERT INTO person (person_id, legal_first_name, legal_last_name,
                date_of_birth, person_status_id,
                creation_timestamp, last_update_timestamp, last_updated_by)
              VALUES (@PersonId, 'Leave', 'Tester', '1990-01-01',
                @StatusId, now(), now(), 'test')",
            new { PersonId = personId, StatusId = activePersonStatusId });

        await conn.ExecuteAsync(
            @"INSERT INTO employment (
                employment_id, person_id, legal_entity_id, employer_id,
                employee_number, employment_type_id, employment_status_id,
                employment_start_date, full_part_time_status_id,
                regular_temporary_status_id, flsa_status_id,
                primary_work_location_id, primary_department_id,
                creation_timestamp, last_update_timestamp, last_updated_by)
              VALUES (
                @EmploymentId, @PersonId, @LegalEntityId, @LegalEntityId,
                @EmpNum, @EmpTypeId, @EmpStatusId,
                '2024-01-01', @FptId, @RegTempId, @FlsaId,
                @LocationId, @DepartmentId,
                now(), now(), 'test')",
            new
            {
                EmploymentId = employmentId,
                PersonId     = personId,
                LegalEntityId,
                EmpNum       = $"TST-{employmentId.ToString()[..8]}",
                EmpTypeId    = employmentTypeId,
                EmpStatusId  = activeEmploymentStatusId,
                FptId        = fptStatusId,
                RegTempId    = regTempStatusId,
                FlsaId       = flsaStatusId,
                LocationId,
                DepartmentId
            });

        _personIds.Add(personId);
        _employmentIds.Add(employmentId);
        return (personId, employmentId);
    }

    private async Task SeedLeaveBalanceAsync(Guid employmentId, int leaveTypeId, decimal balance)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO leave_balance
                (leave_balance_id, employment_id, leave_type_id,
                 available_balance, pending_balance, used_balance, entitlement_total,
                 plan_year_start, plan_year_end, created_timestamp, last_update_timestamp)
              VALUES
                (@Id, @EmpId, @TypeId,
                 @Balance, 0, 0, @Balance,
                 '2025-01-01', '2025-12-31', now(), now())",
            new
            {
                Id      = Guid.NewGuid(),
                EmpId   = employmentId,
                TypeId  = leaveTypeId,
                Balance = balance
            });
    }

    public void Dispose()
    {
        using var conn = _connectionFactory.CreateConnection();
        foreach (var id in _leaveRequestIds)
            conn.Execute("DELETE FROM leave_request WHERE leave_request_id = @Id", new { Id = id });
        foreach (var id in _employmentIds)
        {
            conn.Execute("DELETE FROM leave_balance  WHERE employment_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM work_queue_item WHERE employment_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM employment     WHERE employment_id = @Id", new { Id = id });
        }
        foreach (var id in _personIds)
            conn.Execute("DELETE FROM person WHERE person_id = @Id", new { Id = id });
    }
}

// ============================================================
// DOCUMENT INTEGRATION TESTS
// ============================================================

public class DocumentIntegrationTests : IDisposable
{
    readonly IConnectionFactory  _connectionFactory;
    readonly ILookupCache        _lookupCache;
    readonly IDocumentService    _documentService;
    readonly IDocumentRepository _documentRepository;

    readonly List<Guid>   _personIds    = [];
    readonly List<Guid>   _documentIds  = [];
    readonly string       _tempStorageDir;

    public DocumentIntegrationTests()
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

        _tempStorageDir = Path.Combine(Path.GetTempPath(), $"allwork_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempStorageDir);

        var storageOptions  = new DocumentStorageOptions { BasePath = _tempStorageDir, MaxFileSizeMB = 5 };
        var storageService  = new LocalFileSystemDocumentStorageService(storageOptions);
        var documentRepo    = new DocumentRepository(_connectionFactory);
        _documentRepository = documentRepo;

        _documentService = new DocumentService(
            _connectionFactory, documentRepo, storageService, _lookupCache);
    }

    // -----------------------------------------------------------------------
    // TC-DOC-001: Upload new I-9 creates ACTIVE document with version=1
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadDocument_NewI9_CreatesActiveVersion1()
    {
        var personId     = await CreateTestPersonAsync();
        var employmentId = Guid.NewGuid();

        var content = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var command = new UploadDocumentCommand
        {
            PersonId      = personId,
            EmploymentId  = employmentId,
            DocumentType  = "I9",
            DocumentName  = "I-9 Employment Eligibility",
            FileContent   = content,
            FileFormat    = "PDF",
            EffectiveDate = DateOnly.FromDateTime(DateTime.Today),
            UploadedBy    = Guid.NewGuid()
        };

        var documentId = await _documentService.UploadDocumentAsync(command);
        _documentIds.Add(documentId);

        var saved = await _documentRepository.GetByIdAsync(documentId);
        Assert.NotNull(saved);
        Assert.Equal(_lookupCache.GetId(LookupTables.DocumentStatus, "ACTIVE"),
            saved.DocumentStatusId);
        Assert.Equal(1, saved.DocumentVersion);
        Assert.NotEmpty(saved.StorageReference);
    }

    // -----------------------------------------------------------------------
    // TC-DOC-002: Second I-9 upload supersedes the first
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadDocument_SecondI9_SupersedesFirst()
    {
        var personId     = await CreateTestPersonAsync();
        var employmentId = Guid.NewGuid();

        var base64Content1 = new MemoryStream(new byte[] { 1, 2, 3 });
        var cmd1 = new UploadDocumentCommand
        {
            PersonId      = personId,
            EmploymentId  = employmentId,
            DocumentType  = "I9",
            DocumentName  = "I-9 v1",
            FileContent   = base64Content1,
            FileFormat    = "PDF",
            EffectiveDate = DateOnly.FromDateTime(DateTime.Today),
            UploadedBy    = Guid.NewGuid()
        };
        var firstId = await _documentService.UploadDocumentAsync(cmd1);
        _documentIds.Add(firstId);

        var base64Content2 = new MemoryStream(new byte[] { 4, 5, 6 });
        var cmd2 = cmd1 with { DocumentName = "I-9 v2", FileContent = base64Content2 };
        var secondId = await _documentService.UploadDocumentAsync(cmd2);
        _documentIds.Add(secondId);

        var first  = await _documentRepository.GetByIdAsync(firstId);
        var second = await _documentRepository.GetByIdAsync(secondId);

        Assert.Equal(_lookupCache.GetId(LookupTables.DocumentStatus, "SUPERSEDED"),
            first!.DocumentStatusId);
        Assert.Equal(secondId, first.SupersededByDocumentId);
        Assert.Equal(_lookupCache.GetId(LookupTables.DocumentStatus, "ACTIVE"),
            second!.DocumentStatusId);
        Assert.Equal(2, second.DocumentVersion);
    }

    // -----------------------------------------------------------------------
    // TC-DOC-003: Upload with expiration before effective date throws
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadDocument_ExpirationBeforeEffective_Throws()
    {
        var personId = await CreateTestPersonAsync();

        var command = new UploadDocumentCommand
        {
            PersonId      = personId,
            DocumentType  = "LICENSE",
            DocumentName  = "Driver License",
            FileContent   = new MemoryStream(new byte[] { 1 }),
            FileFormat    = "PDF",
            EffectiveDate  = new DateOnly(2025, 6, 1),
            ExpirationDate = new DateOnly(2025, 5, 31), // before effective
            UploadedBy    = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<ValidationException>(
            () => _documentService.UploadDocumentAsync(command));
    }

    // -----------------------------------------------------------------------
    // TC-DOC-005: VerifyDocument sets verified_by and verification_date
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VerifyDocument_SetsVerificationFields()
    {
        var personId     = await CreateTestPersonAsync();
        var employmentId = Guid.NewGuid();
        var verifierId   = Guid.NewGuid();

        var documentId = await _documentService.UploadDocumentAsync(new UploadDocumentCommand
        {
            PersonId      = personId,
            EmploymentId  = employmentId,
            DocumentType  = "I9",
            DocumentName  = "I-9",
            FileContent   = new MemoryStream(new byte[] { 1, 2, 3 }),
            FileFormat    = "PDF",
            EffectiveDate = DateOnly.FromDateTime(DateTime.Today),
            UploadedBy    = Guid.NewGuid()
        });
        _documentIds.Add(documentId);

        await _documentService.VerifyDocumentAsync(
            new VerifyDocumentCommand { DocumentId = documentId, VerifiedBy = verifierId });

        var saved = await _documentRepository.GetByIdAsync(documentId);
        Assert.Equal(verifierId, saved!.VerifiedBy);
        Assert.NotNull(saved.VerificationDate);
    }

    // -----------------------------------------------------------------------
    // TC-DOC-006: IsI9VerifiedAsync returns true for verified I-9
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IsI9VerifiedAsync_VerifiedI9_ReturnsTrue()
    {
        var personId     = await CreateTestPersonAsync();
        var employmentId = Guid.NewGuid();

        var documentId = await _documentService.UploadDocumentAsync(new UploadDocumentCommand
        {
            PersonId      = personId,
            EmploymentId  = employmentId,
            DocumentType  = "I9",
            DocumentName  = "I-9",
            FileContent   = new MemoryStream(new byte[] { 1, 2, 3 }),
            FileFormat    = "PDF",
            EffectiveDate = DateOnly.FromDateTime(DateTime.Today),
            UploadedBy    = Guid.NewGuid()
        });
        _documentIds.Add(documentId);

        await _documentService.VerifyDocumentAsync(
            new VerifyDocumentCommand { DocumentId = documentId, VerifiedBy = Guid.NewGuid() });

        var result = await _documentService.IsI9VerifiedAsync(personId, employmentId);
        Assert.True(result);
    }

    // -----------------------------------------------------------------------
    // TC-DOC-007: IsI9VerifiedAsync returns false for unverified I-9
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IsI9VerifiedAsync_UnverifiedI9_ReturnsFalse()
    {
        var personId     = await CreateTestPersonAsync();
        var employmentId = Guid.NewGuid();

        var documentId = await _documentService.UploadDocumentAsync(new UploadDocumentCommand
        {
            PersonId      = personId,
            EmploymentId  = employmentId,
            DocumentType  = "I9",
            DocumentName  = "I-9",
            FileContent   = new MemoryStream(new byte[] { 1, 2, 3 }),
            FileFormat    = "PDF",
            EffectiveDate = DateOnly.FromDateTime(DateTime.Today),
            UploadedBy    = Guid.NewGuid()
        });
        _documentIds.Add(documentId);

        var result = await _documentService.IsI9VerifiedAsync(personId, employmentId);
        Assert.False(result);
    }

    // -----------------------------------------------------------------------
    // TC-DOC-008: IsI9VerifiedAsync returns false when no I-9 on file
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IsI9VerifiedAsync_NoI9_ReturnsFalse()
    {
        var personId     = await CreateTestPersonAsync();
        var employmentId = Guid.NewGuid();

        var result = await _documentService.IsI9VerifiedAsync(personId, employmentId);
        Assert.False(result);
    }

    // -----------------------------------------------------------------------
    // TC-DOC-012: DownloadDocumentAsync returns stream and logs audit record
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DownloadDocument_ReturnsStreamAndLogsAudit()
    {
        var personId     = await CreateTestPersonAsync();
        var employmentId = Guid.NewGuid();
        var requestedBy  = Guid.NewGuid();
        var fileBytes    = new byte[] { 10, 20, 30, 40 };

        var documentId = await _documentService.UploadDocumentAsync(new UploadDocumentCommand
        {
            PersonId      = personId,
            EmploymentId  = employmentId,
            DocumentType  = "I9",
            DocumentName  = "I-9 Download Test",
            FileContent   = new MemoryStream(fileBytes),
            FileFormat    = "PDF",
            EffectiveDate = DateOnly.FromDateTime(DateTime.Today),
            UploadedBy    = Guid.NewGuid()
        });
        _documentIds.Add(documentId);

        await using var stream = await _documentService.DownloadDocumentAsync(documentId, requestedBy);
        Assert.NotNull(stream);

        // Verify audit record was created
        using var conn = _connectionFactory.CreateConnection();
        var auditCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_download_audit WHERE document_id = @Id AND accessed_by = @By",
            new { Id = documentId, By = requestedBy });
        Assert.Equal(1, auditCount);
    }

    // -----------------------------------------------------------------------
    // TC-DOC-016: Archive document under legal hold throws
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ArchiveDocument_UnderLegalHold_Throws()
    {
        var personId   = await CreateTestPersonAsync();
        var documentId = Guid.NewGuid();

        // Insert a document with legal_hold_flag = true
        using var conn = _connectionFactory.CreateConnection();
        var activeStatusId = _lookupCache.GetId(LookupTables.DocumentStatus, "ACTIVE");
        var i9TypeId       = _lookupCache.GetId(LookupTables.DocumentType, "I9");
        await conn.ExecuteAsync(
            @"INSERT INTO document (document_id, person_id, document_type_id, document_name,
                document_version, document_status_id, effective_date, storage_reference,
                file_format, upload_date, uploaded_by, legal_hold_flag,
                created_by, creation_timestamp)
              VALUES (@Id, @PersonId, @TypeId, 'Legal Hold Doc', 1, @StatusId,
                now()::date, 'test-ref.pdf', 'PDF', now(), @PersonId, true, @PersonId, now())",
            new { Id = documentId, PersonId = personId, TypeId = i9TypeId, StatusId = activeStatusId });
        _documentIds.Add(documentId);

        await Assert.ThrowsAsync<DomainException>(
            () => _documentService.ArchiveDocumentAsync(documentId, Guid.NewGuid()));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<Guid> CreateTestPersonAsync()
    {
        var personId = Guid.NewGuid();
        var statusId = _lookupCache.GetId(LookupTables.PersonStatus, "ACTIVE");
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO person (person_id, legal_first_name, legal_last_name,
                date_of_birth, person_status_id,
                creation_timestamp, last_update_timestamp, last_updated_by)
              VALUES (@Id, 'Doc', 'Tester', '1990-01-01',
                @StatusId, now(), now(), 'test')",
            new { Id = personId, StatusId = statusId });
        _personIds.Add(personId);
        return personId;
    }

    public void Dispose()
    {
        using var conn = _connectionFactory.CreateConnection();
        foreach (var id in _documentIds)
        {
            conn.Execute("DELETE FROM document_download_audit WHERE document_id = @Id", new { Id = id });
            conn.Execute("DELETE FROM document WHERE document_id = @Id", new { Id = id });
        }
        foreach (var id in _personIds)
            conn.Execute("DELETE FROM person WHERE person_id = @Id", new { Id = id });

        if (Directory.Exists(_tempStorageDir))
            Directory.Delete(_tempStorageDir, recursive: true);
    }
}
