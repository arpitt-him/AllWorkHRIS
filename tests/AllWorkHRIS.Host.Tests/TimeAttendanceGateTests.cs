// tests/AllWorkHRIS.Host.Tests/TimeAttendanceGateTests.cs
// Phase 8 gate — TC-TA-001 through TC-TA-020
//
// TC-TA-011 through TC-TA-014 require allworkhris_dev to be running with
// migration 013 applied. TC-TA-001 through TC-TA-010 and TC-TA-015 through
// TC-TA-020 are pure unit tests with no database dependency.

using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Navigation;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.TimeAttendance;
using AllWorkHRIS.Module.TimeAttendance.Commands;
using AllWorkHRIS.Module.TimeAttendance.Domain;
using AllWorkHRIS.Module.TimeAttendance.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Phase 8 gate — Time &amp; Attendance module.
/// Requires allworkhris_dev running with migration 013 applied for TC-TA-011 through TC-TA-014.
/// </summary>
public sealed class TimeAttendanceGateTests : IAsyncLifetime
{
    private IConnectionFactory _connectionFactory = default!;
    private ITemporalContext   _temporal          = default!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
            "Host=localhost;Database=allworkhris_dev;Username=postgres;Password=dev");
        Environment.SetEnvironmentVariable("DATABASE_PROVIDER", "postgresql");

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new TestDateOnlyHandler());
        SqlMapper.AddTypeHandler(new TestNullableDateOnlyHandler());

        _connectionFactory = new ConnectionFactory();
        _temporal          = new SystemTemporalContext();

        await Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -----------------------------------------------------------------------
    // TC-TA-001 — TimeEntry.Create() produces entry with correct EmploymentId,
    //             Duration, and WorkDate from the command.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_001_TimeEntry_Create_MapsCommandFields()
    {
        var empId    = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 5, 1);

        var cmd = new SubmitTimeEntryCommand
        {
            EmploymentId    = empId,
            PayrollPeriodId = periodId,
            WorkDate        = workDate,
            TimeCategory    = "REGULAR",
            Duration        = 8m,
            EntryMethod     = "SELF_SERVICE",
            SubmittedBy     = empId
        };

        var entry = TimeEntry.Create(cmd, statusId: 1, timeCategoryId: 10, entryMethodId: 5);

        Assert.Equal(empId,    entry.EmploymentId);
        Assert.Equal(periodId, entry.PayrollPeriodId);
        Assert.Equal(workDate, entry.WorkDate);
        Assert.Equal(8m,       entry.Duration);
        Assert.Equal(1,        entry.StatusId);
        Assert.Equal(10,       entry.TimeCategoryId);
        Assert.Equal(5,        entry.EntryMethodId);
    }

    // -----------------------------------------------------------------------
    // TC-TA-002 — TimeEntry.Create() assigns a non-empty GUID as TimeEntryId,
    //             and two successive calls produce different IDs.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_002_TimeEntry_Create_AssignsUniqueId()
    {
        var empId = Guid.NewGuid();
        var cmd   = new SubmitTimeEntryCommand
        {
            EmploymentId    = empId,
            PayrollPeriodId = Guid.NewGuid(),
            WorkDate        = new DateOnly(2026, 5, 1),
            TimeCategory    = "REGULAR",
            Duration        = 8m,
            EntryMethod     = "MANUAL",
            SubmittedBy     = empId
        };

        var a = TimeEntry.Create(cmd, 1, 10, 5);
        var b = TimeEntry.Create(cmd, 1, 10, 5);

        Assert.NotEqual(Guid.Empty, a.TimeEntryId);
        Assert.NotEqual(a.TimeEntryId, b.TimeEntryId);
    }

    // -----------------------------------------------------------------------
    // TC-TA-003 — TimeEntry.CreateCorrection() links OriginalTimeEntryId and
    //             copies EmploymentId from the original.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_003_TimeEntry_CreateCorrection_LinksOriginal()
    {
        var empId = Guid.NewGuid();
        var originalCmd = new SubmitTimeEntryCommand
        {
            EmploymentId    = empId,
            PayrollPeriodId = Guid.NewGuid(),
            WorkDate        = new DateOnly(2026, 4, 15),
            TimeCategory    = "REGULAR",
            Duration        = 8m,
            EntryMethod     = "MANUAL",
            SubmittedBy     = empId
        };
        var original = TimeEntry.Create(originalCmd, 5, 10, 1); // StatusId=5 (LOCKED)

        var corrCmd = new CorrectTimeEntryCommand
        {
            OriginalTimeEntryId = original.TimeEntryId,
            WorkDate            = original.WorkDate,
            TimeCategory        = "REGULAR",
            Duration            = 7.5m,
            CorrectionReason    = "Data entry error",
            CorrectedBy         = Guid.NewGuid()
        };
        var correction = TimeEntry.CreateCorrection(original, corrCmd,
            submittedStatusId: 2, timeCategoryId: 10);

        Assert.Equal(original.TimeEntryId,    correction.OriginalTimeEntryId);
        Assert.Equal(original.EmploymentId,   correction.EmploymentId);
        Assert.Equal(original.PayrollPeriodId, correction.PayrollPeriodId);
        Assert.Equal(7.5m,                    correction.Duration);
        Assert.NotEqual(original.TimeEntryId, correction.TimeEntryId);
    }

    // -----------------------------------------------------------------------
    // TC-TA-004 — OvertimeDetectionResult.NotApplicable() sets IsApplicable=false
    //             and OvertimeDetected=false.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_004_OvertimeResult_NotApplicable()
    {
        var empId  = Guid.NewGuid();
        var result = OvertimeDetectionResult.NotApplicable(empId);

        Assert.Equal(empId, result.IsApplicableFor);
        Assert.False(result.IsApplicable);
        Assert.False(result.OvertimeDetected);
        Assert.Equal(0m, result.TotalRegularHours);
        Assert.Empty(result.ReclassifiedEntryIds);
    }

    // -----------------------------------------------------------------------
    // TC-TA-005 — OvertimeDetectionResult.NoOvertime() has IsApplicable=true,
    //             OvertimeDetected=false, and correct total hours.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_005_OvertimeResult_NoOvertime()
    {
        var empId  = Guid.NewGuid();
        var result = OvertimeDetectionResult.NoOvertime(empId, 38.5m);

        Assert.True(result.IsApplicable);
        Assert.False(result.OvertimeDetected);
        Assert.Equal(38.5m, result.TotalRegularHours);
        Assert.Equal(0m, result.OvertimeHours);
    }

    // -----------------------------------------------------------------------
    // TC-TA-006 — OvertimeDetectionResult.WithOvertime() sets OvertimeDetected=true
    //             and records reclassified entry IDs.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_006_OvertimeResult_WithOvertime()
    {
        var empId           = Guid.NewGuid();
        var reclassifiedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var result          = OvertimeDetectionResult.WithOvertime(empId, 40m, 3m, reclassifiedIds);

        Assert.True(result.IsApplicable);
        Assert.True(result.OvertimeDetected);
        Assert.Equal(40m, result.TotalRegularHours);
        Assert.Equal(3m,  result.OvertimeHours);
        Assert.Equal(2,   result.ReclassifiedEntryIds.Count);
    }

    // -----------------------------------------------------------------------
    // TC-TA-007 — TimeEntryStatus enum contains all required lifecycle values.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_007_TimeEntryStatus_ContainsAllValues()
    {
        var names = Enum.GetNames<TimeEntryStatus>();

        Assert.Contains("Draft",     names);
        Assert.Contains("Submitted", names);
        Assert.Contains("Approved",  names);
        Assert.Contains("Rejected",  names);
        Assert.Contains("Locked",    names);
        Assert.Contains("Consumed",  names);
        Assert.Contains("Void",      names);
        Assert.Contains("Corrected", names);
    }

    // -----------------------------------------------------------------------
    // TC-TA-008 — TimeCategory enum contains all standard leave and work types.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_008_TimeCategory_ContainsAllValues()
    {
        var names = Enum.GetNames<TimeCategory>();

        Assert.Contains("Regular",     names);
        Assert.Contains("Overtime",    names);
        Assert.Contains("Pto",         names);
        Assert.Contains("Sick",        names);
        Assert.Contains("Holiday",     names);
        Assert.Contains("Bereavement", names);
        Assert.Contains("JuryDuty",    names);
        Assert.Contains("Unpaid",      names);
    }

    // -----------------------------------------------------------------------
    // TC-TA-009 — EntryMethod enum contains all supported entry mechanisms.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_009_EntryMethod_ContainsAllValues()
    {
        var names = Enum.GetNames<EntryMethod>();

        Assert.Contains("Manual",      names);
        Assert.Contains("Import",      names);
        Assert.Contains("Api",         names);
        Assert.Contains("SelfService", names);
    }

    // -----------------------------------------------------------------------
    // TC-TA-010 — TimeAttendanceDashboardContributor.RequiredRoles contains
    //             TimeAdmin, Manager, and TimeViewer.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_TA_010_DashboardContributor_RequiredRoles()
    {
        var contributor = new TimeAttendanceDashboardContributor(
            _connectionFactory,
            _temporal,
            NullLogger<TimeAttendanceDashboardContributor>.Instance);

        Assert.Contains("TimeAdmin",   contributor.RequiredRoles);
        Assert.Contains("Manager",     contributor.RequiredRoles);
        Assert.Contains("TimeViewer",  contributor.RequiredRoles);

        // Non-T&A roles are not in the required list
        Assert.DoesNotContain("PayrollOperator", contributor.RequiredRoles);
        Assert.DoesNotContain("BenefitsAdmin",   contributor.RequiredRoles);

        // Call completes without throwing (empty DB returns empty list)
        var items = await contributor.GetItemsAsync(null, ["TimeAdmin"]);
        Assert.NotNull(items);
    }

    // -----------------------------------------------------------------------
    // TC-TA-011 — TimeAttendanceDashboardContributor.GetItemsAsync returns
    //             empty or valid list against dev DB without throwing.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_TA_011_DashboardContributor_GetItemsAsync_DoesNotThrow()
    {
        var contributor = new TimeAttendanceDashboardContributor(
            _connectionFactory,
            _temporal,
            NullLogger<TimeAttendanceDashboardContributor>.Instance);

        var items = await contributor.GetItemsAsync(Guid.NewGuid(), ["TimeAdmin", "Manager"]);

        Assert.NotNull(items);
        // When no pending entries exist the result should be empty
        // When pending entries exist the first item routes to /ta/timecards
        foreach (var item in items)
            Assert.Equal("/ta/timecards", item.Route);
    }

    // -----------------------------------------------------------------------
    // TC-TA-012 — TimeAttendanceNavContributor.Target == EntityNav.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_012_NavContributor_Target_IsEntityNav()
    {
        var nav = new TimeAttendanceNavContributor();
        Assert.Equal(NavTarget.EntityNav, nav.Target);
    }

    // -----------------------------------------------------------------------
    // TC-TA-013 — TimeAttendanceNavContributor with TimeAdmin role returns a
    //             section containing Timecards and Payroll Handoff. My Timecard
    //             is only added when the user also holds Employee role.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_013_NavContributor_TimeAdminGetsTimecardsAndHandoff()
    {
        var nav     = new TimeAttendanceNavContributor();
        var section = nav.GetSection(["TimeAdmin"]);

        Assert.NotNull(section);
        Assert.Equal("Time Tracking", section!.Label);
        Assert.Equal(25, section.Order);

        var hrefs = section.Items.Select(i => i.Href).ToList();
        Assert.Contains("/ta/timecards", hrefs);
        Assert.Contains("/ta/handoff",   hrefs);
        Assert.DoesNotContain("/ta/my-timecard", hrefs);

        // TimeAdmin + Employee gets all three
        var fullSection = nav.GetSection(["TimeAdmin", "Employee"]);
        Assert.NotNull(fullSection);
        var fullHrefs = fullSection!.Items.Select(i => i.Href).ToList();
        Assert.Contains("/ta/timecards",   fullHrefs);
        Assert.Contains("/ta/my-timecard", fullHrefs);
        Assert.Contains("/ta/handoff",     fullHrefs);
    }

    // -----------------------------------------------------------------------
    // TC-TA-014 — TimeAttendanceNavContributor with Employee-only role returns
    //             section with My Timecard but NOT Timecards or Payroll Handoff.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_014_NavContributor_EmployeeOnlySeesMyTimecard()
    {
        var nav     = new TimeAttendanceNavContributor();
        var section = nav.GetSection(["Employee"]);

        Assert.NotNull(section);
        var routes = section!.Items.Select(i => i.Href).ToList();

        Assert.Contains("/ta/my-timecard", routes);
        Assert.DoesNotContain("/ta/timecards", routes);
        Assert.DoesNotContain("/ta/handoff",   routes);
    }

    // -----------------------------------------------------------------------
    // TC-TA-015 — TimeAttendanceNavContributor returns null for users with
    //             no T&A role.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_015_NavContributor_NullForNoTaRole()
    {
        var nav = new TimeAttendanceNavContributor();

        Assert.Null(nav.GetSection(["PayrollOperator", "BenefitsAdmin"]));
        Assert.Null(nav.GetSection([]));
        Assert.Null(nav.GetSection(["HrisViewer"]));
    }

    // -----------------------------------------------------------------------
    // TC-TA-016 — NullTimeApprovalNotifier completes all notification methods
    //             without throwing.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_TA_016_NullNotifier_CompletesWithoutThrowing()
    {
        var notifier = new NullTimeApprovalNotifier();
        var empId    = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var weekStart= new DateOnly(2026, 5, 4);

        await notifier.NotifyTimeApprovalAsync(entryId, empId);
        await notifier.NotifyOvertimeWarningAsync(empId, weekStart, 3.5m);
        await notifier.NotifyRetroCalculationReviewAsync(entryId, empId, periodId);
    }

    // -----------------------------------------------------------------------
    // TC-TA-017 — HandoffResult record preserves Delivered, Failed, TotalHours.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_017_HandoffResult_Fields()
    {
        var periodId = Guid.NewGuid();
        var runId    = Guid.NewGuid();
        var result   = new HandoffResult(periodId, runId, Delivered: 12, Failed: 1, TotalHours: 96.5m);

        Assert.Equal(periodId, result.PeriodId);
        Assert.Equal(runId,    result.PayrollRunId);
        Assert.Equal(12,       result.Delivered);
        Assert.Equal(1,        result.Failed);
        Assert.Equal(96.5m,    result.TotalHours);
    }

    // -----------------------------------------------------------------------
    // TC-TA-018 — TimeAttendanceDashboardContributor.AccentColor matches
    //             the module CSS variable.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_018_DashboardContributor_AccentColor()
    {
        var contributor = new TimeAttendanceDashboardContributor(
            _connectionFactory,
            _temporal,
            NullLogger<TimeAttendanceDashboardContributor>.Instance);

        Assert.Equal("var(--module-ta, #7c3aed)", contributor.AccentColor);
        Assert.Equal("T&A", contributor.ModuleName);
    }

    // -----------------------------------------------------------------------
    // TC-TA-019 — TimeAttendanceNavContributor: Manager role sees Timecards
    //             but not Payroll Handoff.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_019_NavContributor_ManagerSeesTimecards_NotHandoff()
    {
        var nav     = new TimeAttendanceNavContributor();
        var section = nav.GetSection(["Manager"]);

        Assert.NotNull(section);
        var routes = section!.Items.Select(i => i.Href).ToList();

        Assert.Contains("/ta/timecards",      routes);
        Assert.DoesNotContain("/ta/handoff",  routes);
        Assert.DoesNotContain("/ta/my-timecard", routes);
    }

    // -----------------------------------------------------------------------
    // TC-TA-020 — TimeViewer role sees Timecards section; TimeViewer + Employee
    //             combined sees both Timecards and My Timecard.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_TA_020_NavContributor_TimeViewerPlusEmployeeSeesBothSections()
    {
        var nav            = new TimeAttendanceNavContributor();
        var viewerSection  = nav.GetSection(["TimeViewer"]);
        var combinedSection= nav.GetSection(["TimeViewer", "Employee"]);

        Assert.NotNull(viewerSection);
        var viewerRoutes = viewerSection!.Items.Select(i => i.Href).ToList();
        Assert.Contains("/ta/timecards", viewerRoutes);

        Assert.NotNull(combinedSection);
        var combinedRoutes = combinedSection!.Items.Select(i => i.Href).ToList();
        Assert.Contains("/ta/timecards",   combinedRoutes);
        Assert.Contains("/ta/my-timecard", combinedRoutes);
    }
}
