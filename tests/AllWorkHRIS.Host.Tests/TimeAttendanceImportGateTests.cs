// tests/AllWorkHRIS.Host.Tests/TimeAttendanceImportGateTests.cs
// Phase 8.1 gate — TC-TA-IMP-001 through TC-TA-IMP-006
//
// TC-TA-IMP-001/002 are pure unit tests with no database dependency.
// TC-TA-IMP-003 through TC-TA-IMP-005 require allworkhris_dev running with
// migrations 001–014 applied and the lookup tables seeded.
// TC-TA-IMP-006 is a pure unit test.

using System.Text;
using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Navigation;
using AllWorkHRIS.Module.TimeAttendance;
using AllWorkHRIS.Module.TimeAttendance.Domain;
using AllWorkHRIS.Module.TimeAttendance.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Phase 8.1 gate — T&amp;A Batch Import.
/// </summary>
public sealed class TimeAttendanceImportGateTests : IAsyncLifetime
{
    private IConnectionFactory _connectionFactory = default!;
    private ILookupCache       _lookupCache       = default!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
            "Host=localhost;Database=allworkhris_dev;Username=postgres;Password=dev");
        Environment.SetEnvironmentVariable("DATABASE_PROVIDER", "postgresql");

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new TestDateOnlyHandler());
        SqlMapper.AddTypeHandler(new TestNullableDateOnlyHandler());

        _connectionFactory = new ConnectionFactory();

        var cache = new LookupCache(_connectionFactory);
        await cache.RefreshAsync();
        _lookupCache = cache;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -----------------------------------------------------------------------
    // TC-TA-IMP-001 — TimeImportResult record has correct properties and
    //                 supports construction from code.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_IMP_001_TimeImportResult_RecordStructure()
    {
        var errors = new List<TimeImportError>
        {
            new(2, "EMP-BAD", "Employee 'EMP-BAD' not found."),
            new(4, "EMP-X",   "Unknown time_category 'DANCE_BREAK'.")
        };

        var result = new TimeImportResult(Imported: 3, Failed: 2, Errors: errors);

        Assert.Equal(3, result.Imported);
        Assert.Equal(2, result.Failed);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(2,          result.Errors[0].RowNumber);
        Assert.Equal("EMP-BAD",  result.Errors[0].EmployeeNumber);
        Assert.Contains("not found", result.Errors[0].Reason);
        Assert.Equal(4,          result.Errors[1].RowNumber);
        Assert.Contains("DANCE_BREAK", result.Errors[1].Reason);
    }

    // -----------------------------------------------------------------------
    // TC-TA-IMP-002 — ImportAsync with header-only CSV returns 0 imported,
    //                 0 failed (empty batch is a no-op).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_TA_IMP_002_EmptyCsv_ReturnsZeroZero()
    {
        var service = BuildService();

        const string csv = "employee_number,work_date,time_category,duration,start_time,end_time,payroll_period_id,project_code,task_code,notes\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await service.ImportAsync(stream, Guid.NewGuid());

        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);
    }

    // -----------------------------------------------------------------------
    // TC-TA-IMP-003 — Row with negative duration fails immediately;
    //                 no DB lookup is attempted for that row.
    //                 (Validation short-circuits before employee/period queries.)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_TA_IMP_003_NegativeDuration_RowFails()
    {
        var service = BuildService();

        var csv = CsvHeader() + "\n" +
                  $"EMP-NOTFOUND,2027-12-01,REGULAR,-2.00,,,{Guid.NewGuid()},,," + "\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await service.ImportAsync(stream, Guid.NewGuid());

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.Contains("positive", result.Errors[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // TC-TA-IMP-004 — Row with unknown time_category fails before any DB
    //                 lookup is attempted; error reason names the bad code.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_TA_IMP_004_UnknownCategory_RowFails()
    {
        var service = BuildService();

        var csv = CsvHeader() + "\n" +
                  $"EMP-NOTFOUND,2027-12-01,DANCE_BREAK,8.00,,,{Guid.NewGuid()},,," + "\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await service.ImportAsync(stream, Guid.NewGuid());

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.Contains("DANCE_BREAK", result.Errors[0].Reason);
    }

    // -----------------------------------------------------------------------
    // TC-TA-IMP-005 — Row with employee_number that does not exist in the DB
    //                 produces an error whose reason mentions "not found".
    //                 Requires allworkhris_dev (employment table must be live).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_TA_IMP_005_UnknownEmployee_RowFails()
    {
        var service = BuildService();

        // EMP-NOTFOUND is guaranteed not to exist in any properly seeded dev DB
        var csv = CsvHeader() + "\n" +
                  $"EMP-NOTFOUND,2027-12-01,REGULAR,8.00,,,{Guid.NewGuid()},,," + "\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await service.ImportAsync(stream, Guid.NewGuid());

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.Equal("EMP-NOTFOUND", result.Errors[0].EmployeeNumber);
        Assert.Contains("not found", result.Errors[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // TC-TA-IMP-006 — TimeAttendanceNavContributor includes /ta/import for
    //                 TimeAdmin role and excludes it for Employee-only.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_IMP_006_NavContributor_ImportLink_OnlyForTimeAdmin()
    {
        var contributor = new TimeAttendanceNavContributor();

        // TimeAdmin should see Import Entries
        var adminSection = contributor.GetSection(["TimeAdmin"]);
        Assert.NotNull(adminSection);
        Assert.Contains(adminSection!.Items, i => i.Href == "/ta/import");

        // Employee-only should NOT see Import Entries
        var employeeSection = contributor.GetSection(["Employee"]);
        Assert.NotNull(employeeSection);
        Assert.DoesNotContain(employeeSection!.Items, i => i.Href == "/ta/import");

        // TimeViewer / Manager should NOT see Import Entries
        var viewerSection = contributor.GetSection(["TimeViewer"]);
        Assert.NotNull(viewerSection);
        Assert.DoesNotContain(viewerSection!.Items, i => i.Href == "/ta/import");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private ITimeImportService BuildService()
    {
        var entryRepo    = new AllWorkHRIS.Module.TimeAttendance.Repositories.TimeEntryRepository(
                               _connectionFactory, _lookupCache);
        var notifier     = new NullTimeApprovalNotifier();
        var otService    = new OvertimeDetectionService(entryRepo, _connectionFactory,
                               _lookupCache, notifier);
        var entryService = new TimeEntryService(entryRepo, otService, _connectionFactory,
                               _lookupCache, notifier, NullLogger<TimeEntryService>.Instance);
        return new TimeImportService(_connectionFactory, _lookupCache, entryService);
    }

    private static string CsvHeader() =>
        "employee_number,work_date,time_category,duration,start_time,end_time,payroll_period_id,project_code,task_code,notes";
}
