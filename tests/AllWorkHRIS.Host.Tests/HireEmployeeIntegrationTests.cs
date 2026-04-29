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

public class HireEmployeeIntegrationTests : IDisposable
{
    // Seed data IDs already in allworkhris_dev
    static readonly Guid LegalEntityId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    static readonly Guid DepartmentId  = Guid.Parse("10000000-0000-0000-0000-000000000003");
    static readonly Guid LocationId    = Guid.Parse("10000000-0000-0000-0000-000000000004");
    static readonly Guid JobId         = Guid.Parse("20000000-0000-0000-0000-000000000001");

    readonly IConnectionFactory      _connectionFactory;
    readonly IEmploymentService      _employmentService;
    readonly ILookupCache            _lookupCache;

    // Track all created records for cleanup
    readonly List<Guid> _personIds     = [];
    readonly List<Guid> _employmentIds = [];

    public HireEmployeeIntegrationTests()
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

        var personRepo        = new PersonRepository(_connectionFactory);
        var personAddressRepo = new PersonAddressRepository(_connectionFactory);
        var employmentRepo    = new EmploymentRepository(_connectionFactory);
        var assignmentRepo    = new AssignmentRepository(_connectionFactory);
        var compensationRepo  = new CompensationRepository(_connectionFactory);
        var eventRepo         = new EmployeeEventRepository(_connectionFactory);
        var eventPublisher    = new InProcessEventBus();
        var temporalContext   = new SystemTemporalContext();

        _employmentService = new EmploymentService(
            _connectionFactory,
            personRepo,
            personAddressRepo,
            employmentRepo,
            assignmentRepo,
            compensationRepo,
            eventRepo,
            eventPublisher,
            temporalContext,
            _lookupCache);
    }

    // -----------------------------------------------------------------------
    // TC-HRS-001: Hire command creates all records atomically
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HireEmployee_WithValidCommand_PersistsAllRecords()
    {
        var command = BuildCommand($"TEST-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

        var result = await _employmentService.HireEmployeeAsync(command);
        _personIds.Add(result.PersonId);
        _employmentIds.Add(result.EmploymentId);

        Assert.NotEqual(Guid.Empty, result.PersonId);
        Assert.NotEqual(Guid.Empty, result.EmploymentId);
        Assert.NotEqual(Guid.Empty, result.EventId);

        using var conn = _connectionFactory.CreateConnection();

        // Person
        var person = await conn.QueryFirstOrDefaultAsync<Person>(
            "SELECT * FROM person WHERE person_id = @Id", new { Id = result.PersonId });
        Assert.NotNull(person);
        Assert.Equal("Jane",  person.LegalFirstName);
        Assert.Equal("Smith", person.LegalLastName);
        Assert.Equal(_lookupCache.GetId(LookupTables.PersonStatus, "ACTIVE"), person.PersonStatusId);

        // Person address
        var address = await conn.QueryFirstOrDefaultAsync<PersonAddress>(
            "SELECT * FROM person_address WHERE person_id = @Id", new { Id = result.PersonId });
        Assert.NotNull(address);
        Assert.Equal("PRIMARY",      address.AddressType);
        Assert.Equal("123 Test St",  address.AddressLine1);
        Assert.Equal("Springfield",  address.City);
        Assert.Equal("IL",           address.StateCode);
        Assert.Equal("62701",        address.PostalCode);
        Assert.Equal("US",           address.CountryCode);
        Assert.Equal("555-867-5309", address.PhonePrimary);
        Assert.Equal("jane@test.com",address.EmailPersonal);

        // Employment
        var employment = await conn.QueryFirstOrDefaultAsync<Employment>(
            "SELECT * FROM employment WHERE employment_id = @Id", new { Id = result.EmploymentId });
        Assert.NotNull(employment);
        Assert.Equal(_lookupCache.GetId(LookupTables.EmploymentStatus,       "ACTIVE"),     employment.EmploymentStatusId);
        Assert.Equal(_lookupCache.GetId(LookupTables.EmploymentType,         "EMPLOYEE"),   employment.EmploymentTypeId);
        Assert.Equal(_lookupCache.GetId(LookupTables.FlsaStatus,             "NON_EXEMPT"), employment.FlsaStatusId);
        Assert.Equal(_lookupCache.GetId(LookupTables.FullPartTimeStatus,     "FULL_TIME"),  employment.FullPartTimeStatusId);
        Assert.Equal(_lookupCache.GetId(LookupTables.RegularTemporaryStatus, "REGULAR"),    employment.RegularTemporaryStatusId);

        // Assignment
        var assignment = await conn.QueryFirstOrDefaultAsync<Assignment>(
            "SELECT * FROM assignment WHERE employment_id = @Id", new { Id = result.EmploymentId });
        Assert.NotNull(assignment);
        Assert.Equal(_lookupCache.GetId(LookupTables.AssignmentType,   "PRIMARY"), assignment.AssignmentTypeId);
        Assert.Equal(_lookupCache.GetId(LookupTables.AssignmentStatus, "ACTIVE"),  assignment.AssignmentStatusId);

        // Compensation
        var compensation = await conn.QueryFirstOrDefaultAsync<CompensationRecord>(
            "SELECT * FROM compensation_record WHERE employment_id = @Id", new { Id = result.EmploymentId });
        Assert.NotNull(compensation);
        Assert.Equal(25.00m,          compensation.BaseRate);
        Assert.Equal(25.00m * 2080m,  compensation.AnnualEquivalent);  // HOURLY × 2080
        Assert.Equal(_lookupCache.GetId(LookupTables.CompensationRateType, "HOURLY"),   compensation.RateTypeId);
        Assert.Equal(_lookupCache.GetId(LookupTables.PayFrequency,         "BIWEEKLY"), compensation.PayFrequencyId);
        Assert.Equal(_lookupCache.GetId(LookupTables.CompensationStatus,   "ACTIVE"),   compensation.CompensationStatusId);
        Assert.Equal(_lookupCache.GetId(LookupTables.ApprovalStatus,       "APPROVED"), compensation.ApprovalStatusId);
        Assert.True(compensation.PrimaryRateFlag);

        // Employee event
        var hireEvent = await conn.QueryFirstOrDefaultAsync<EmployeeEvent>(
            "SELECT * FROM employee_event WHERE employment_id = @Id", new { Id = result.EmploymentId });
        Assert.NotNull(hireEvent);
        Assert.Equal(_lookupCache.GetId(LookupTables.EmployeeEventType, "HIRE"), hireEvent.EventTypeId);
    }

    // -----------------------------------------------------------------------
    // TC-HRS-002: Duplicate employee number throws DomainException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HireEmployee_WithDuplicateEmployeeNumber_ThrowsDomainException()
    {
        var employeeNumber = $"DUP-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        // First hire — succeeds
        var result = await _employmentService.HireEmployeeAsync(BuildCommand(employeeNumber));
        _personIds.Add(result.PersonId);
        _employmentIds.Add(result.EmploymentId);

        // Second hire — same number, different person
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => _employmentService.HireEmployeeAsync(BuildCommand(employeeNumber, firstName: "Bob")));

        Assert.Contains(employeeNumber, ex.Message);
    }

    // -----------------------------------------------------------------------
    // TC-HRS-003: Missing required field throws ValidationException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HireEmployee_WithBlankLegalFirstName_ThrowsValidationException()
    {
        var command = BuildCommand($"VAL-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            firstName: "");

        await Assert.ThrowsAsync<ValidationException>(
            () => _employmentService.HireEmployeeAsync(command));
    }

    [Fact]
    public async Task HireEmployee_WithZeroBaseRate_ThrowsValidationException()
    {
        var command = BuildCommand($"VAL2-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            baseRate: 0m);

        await Assert.ThrowsAsync<ValidationException>(
            () => _employmentService.HireEmployeeAsync(command));
    }

    // -----------------------------------------------------------------------
    // TC-HRS-024: Hire with PayrollContextId = null succeeds
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HireEmployee_WithNullPayrollContextId_Succeeds()
    {
        var command = BuildCommand($"NPC-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}") with
        {
            PayrollContextId = null
        };

        var result = await _employmentService.HireEmployeeAsync(command);
        _personIds.Add(result.PersonId);
        _employmentIds.Add(result.EmploymentId);

        Assert.NotEqual(Guid.Empty, result.EmploymentId);
    }

    // -----------------------------------------------------------------------
    // TC-HRS-025: HireEventPayload published with no subscribers — no exception
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HireEmployee_PublishesEvent_WithNoSubscribers_NoException()
    {
        var command = BuildCommand($"EVT-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

        var ex = await Record.ExceptionAsync(
            () => _employmentService.HireEmployeeAsync(command).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    _personIds.Add(t.Result.PersonId);
                    _employmentIds.Add(t.Result.EmploymentId);
                }
                return t;
            }));

        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    HireEmployeeCommand BuildCommand(string employeeNumber, string firstName = "Jane",
        decimal baseRate = 25.00m)
        => new()
        {
            LegalFirstName       = firstName,
            LegalLastName        = "Smith",
            DateOfBirth          = new DateOnly(1990, 5, 15),
            NationalIdentifier   = "123-45-6789",
            PhonePrimary         = "555-867-5309",
            EmailPersonal        = "jane@test.com",
            AddressLine1         = "123 Test St",
            City                 = "Springfield",
            StateCode            = "IL",
            PostalCode           = "62701",
            CountryCode          = "US",
            LegalEntityId        = LegalEntityId,
            EmployeeNumber       = employeeNumber,
            EmploymentTypeId     = _lookupCache.GetId(LookupTables.EmploymentType,      "EMPLOYEE"),
            EmploymentStartDate  = new DateOnly(2024, 1, 15),
            FlsaStatusId         = _lookupCache.GetId(LookupTables.FlsaStatus,          "NON_EXEMPT"),
            FullPartTimeStatusId = _lookupCache.GetId(LookupTables.FullPartTimeStatus,  "FULL_TIME"),
            JobId                = JobId,
            DepartmentId         = DepartmentId,
            LocationId           = LocationId,
            RateTypeId           = _lookupCache.GetId(LookupTables.CompensationRateType,"HOURLY"),
            BaseRate             = baseRate,
            PayFrequencyId       = _lookupCache.GetId(LookupTables.PayFrequency,        "BIWEEKLY"),
            ChangeReasonCode     = "NEW_HIRE",
            InitiatedBy          = Guid.NewGuid()
        };

    public void Dispose()
    {
        if (_employmentIds.Count == 0 && _personIds.Count == 0) return;
        using var conn = _connectionFactory.CreateConnection();
        foreach (var id in _employmentIds)
        {
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
}

sealed class TestDateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
        => parameter.Value = value.ToDateTime(TimeOnly.MinValue);

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly d  => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _           => DateOnly.FromDateTime(Convert.ToDateTime(value))
    };
}

sealed class TestNullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        => parameter.Value = value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    public override DateOnly? Parse(object value)
    {
        if (value is DBNull || value is null) return null;
        return value switch
        {
            DateOnly d  => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _           => DateOnly.FromDateTime(Convert.ToDateTime(value))
        };
    }
}
