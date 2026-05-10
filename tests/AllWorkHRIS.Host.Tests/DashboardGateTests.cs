// tests/AllWorkHRIS.Host.Tests/DashboardGateTests.cs
// Phase 7 gate — TC-DASH-001 through TC-DASH-010
//
// TC-DASH-003/004/005/006/007/008/009 test Blazor component behavior (entity lock
// guard, nav surface rendering, deep-link redirect). These are verified manually
// during browser testing; the xUnit tests below cover the service-layer contracts
// that those tests depend on.

using Dapper;
using AllWorkHRIS.Core.Dashboard;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Navigation;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host.Hris.Dashboard;
using AllWorkHRIS.Host.Hris.Navigation;
using AllWorkHRIS.Host.Hris.Repositories;
using AllWorkHRIS.Host.Hris.Services;
using AllWorkHRIS.Host.Config.Navigation;
using AllWorkHRIS.Host.Payroll.Tax;
using AllWorkHRIS.Module.Benefits;
using AllWorkHRIS.Module.Payroll;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Phase 7 gate — Dashboard &amp; Navigation.
/// Requires allworkhris_dev to be running with seed data applied.
/// </summary>
public sealed class DashboardGateTests : IAsyncLifetime
{
    // -----------------------------------------------------------------------
    // Infrastructure
    // -----------------------------------------------------------------------

    private IConnectionFactory  _connectionFactory = default!;
    private ITemporalContext    _temporal          = default!;
    private ILookupCache        _lookupCache       = default!;

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

        var cache = new LookupCache(_connectionFactory);
        await cache.RefreshAsync();
        _lookupCache = cache;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -----------------------------------------------------------------------
    // Helper — build OrgStructureService for HRIS contributors
    // -----------------------------------------------------------------------
    private IOrgStructureService OrgService()
    {
        var orgRepo = new OrgUnitRepository(_connectionFactory);
        return new OrgStructureService(_connectionFactory, orgRepo, _lookupCache, new NullTaxProfileRepository());
    }

    // -----------------------------------------------------------------------
    // TC-DASH-001 — PayrollDashboardContributor: RequiredRoles contains
    //               PayrollOperator; contributor filtered out for non-payroll roles.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_DASH_001_PayrollContributor_FilteredByRole()
    {
        var contributor = new PayrollDashboardContributor(
            _connectionFactory,
            _temporal,
            NullLogger<PayrollDashboardContributor>.Instance);

        // Payroll roles — contributor should be callable
        Assert.Contains("PayrollOperator", contributor.RequiredRoles);
        Assert.Contains("PayrollAdmin",    contributor.RequiredRoles);

        // Non-payroll roles — filtered out at dashboard level
        var nonPayrollRoles = new[] { "HrisViewer", "BenefitsAdmin" };
        var isVisible       = contributor.RequiredRoles.Any(r => nonPayrollRoles.Contains(r));
        Assert.False(isVisible,
            "PayrollDashboardContributor should not be visible to non-payroll roles");

        // Call succeeds and returns a list (may be empty if no qualifying runs)
        var items = await contributor.GetItemsAsync(null, ["PayrollOperator"]);
        Assert.NotNull(items);
    }

    // -----------------------------------------------------------------------
    // TC-DASH-002 — BenefitsDashboardContributor: RequiredRoles contains
    //               BenefitsAdmin; not visible to pure PayrollOperator.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_DASH_002_BenefitsContributor_FilteredByRole()
    {
        var contributor = new BenefitsDashboardContributor(
            _connectionFactory,
            _temporal,
            NullLogger<BenefitsDashboardContributor>.Instance);

        Assert.Contains("BenefitsAdmin", contributor.RequiredRoles);

        // Pure PayrollOperator does not hold BenefitsAdmin or HrisAdmin
        var payrollOnlyRoles = new[] { "PayrollOperator" };
        var isVisible        = contributor.RequiredRoles.Any(r => payrollOnlyRoles.Contains(r));
        Assert.False(isVisible,
            "BenefitsDashboardContributor should not be visible to PayrollOperator-only users");

        var items = await contributor.GetItemsAsync(null, ["BenefitsAdmin"]);
        Assert.NotNull(items);
    }

    // -----------------------------------------------------------------------
    // TC-DASH-003 — HrisSessionState: SetEntity + Lock produces correct state.
    //               (Service-layer contract for "clicking a task locks entity".)
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_DASH_003_SessionState_SetEntityAndLock()
    {
        var state = new HrisSessionState();

        Assert.False(state.IsLocked);
        Assert.Null(state.SelectedLegalEntityId);

        var entityId   = Guid.NewGuid();
        const string entityName = "Acme Corp";

        state.SetEntity(entityId, entityName);
        state.Lock();

        Assert.True(state.IsLocked);
        Assert.Equal(entityId,   state.SelectedLegalEntityId);
        Assert.Equal(entityName, state.SelectedLegalEntityName);
        Assert.True(state.HasEntity);
    }

    // -----------------------------------------------------------------------
    // TC-DASH-004 — HrisSessionState: Unlock clears the lock without losing entity.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_DASH_004_SessionState_UnlockPreservesEntity()
    {
        var state    = new HrisSessionState();
        var entityId = Guid.NewGuid();
        state.SetEntity(entityId, "Test Entity");
        state.Lock();

        state.Unlock();

        Assert.False(state.IsLocked);
        Assert.Equal(entityId, state.SelectedLegalEntityId);
    }

    // -----------------------------------------------------------------------
    // TC-DASH-005 — Entity nav sections are returned for entity-nav contributors
    //               when the user holds the required role.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_DASH_005_EntityNavContributors_ReturnSectionsForCorrectRoles()
    {
        var hrisNav    = new HrisNavContributor();
        var payrollNav = new PayrollNavContributor();
        var benefitsNav= new BenefitsNavContributor();

        Assert.Equal(NavTarget.EntityNav, hrisNav.Target);
        Assert.Equal(NavTarget.EntityNav, payrollNav.Target);
        Assert.Equal(NavTarget.EntityNav, benefitsNav.Target);

        var hrisSection    = hrisNav.GetSection(["HrisViewer"]);
        var payrollSection = payrollNav.GetSection(["PayrollOperator"]);
        var benefitsSection= benefitsNav.GetSection(["BenefitsAdmin"]);

        Assert.NotNull(hrisSection);
        Assert.NotNull(payrollSection);
        Assert.NotNull(benefitsSection);

        Assert.NotEmpty(hrisSection!.Items);
        Assert.NotEmpty(payrollSection!.Items);
        Assert.NotEmpty(benefitsSection!.Items);
    }

    // -----------------------------------------------------------------------
    // TC-DASH-006 — Nav contributors return null when user lacks required role.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_DASH_006_NavContributors_ReturnNullForUnauthorizedRoles()
    {
        var payrollNav  = new PayrollNavContributor();
        var benefitsNav = new BenefitsNavContributor();

        // Employee role holds neither PayrollOperator nor BenefitsAdmin
        var employeeRoles = new[] { "Employee" };

        Assert.Null(payrollNav.GetSection(employeeRoles));
        Assert.Null(benefitsNav.GetSection(employeeRoles));
    }

    // -----------------------------------------------------------------------
    // TC-DASH-007 — HrisDashboardContributor: returns items without throwing
    //               when called for all entities (entityId = null).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task TC_DASH_007_HrisContributor_ReturnsWithoutThrowing()
    {
        var contributor = new HrisDashboardContributor(
            new AllWorkHRIS.Host.Hris.Repositories.DocumentRepository(_connectionFactory),
            OrgService(),
            _temporal,
            NullLogger<HrisDashboardContributor>.Instance);

        var items = await contributor.GetItemsAsync(null, ["HrisViewer"]);
        Assert.NotNull(items);
    }

    // -----------------------------------------------------------------------
    // TC-DASH-008 — SystemAdminNavContributor: returns AdminNav section for
    //               SystemAdmin; returns null for all other roles.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_DASH_008_SystemAdminNavContributor_CorrectTargetAndRoleFilter()
    {
        var contributor = new SystemAdminNavContributor();

        Assert.Equal(NavTarget.AdminNav, contributor.Target);

        var adminSection = contributor.GetSection(["SystemAdmin"]);
        Assert.NotNull(adminSection);
        Assert.NotEmpty(adminSection!.Items);

        Assert.Null(contributor.GetSection(["PayrollAdmin"]));
        Assert.Null(contributor.GetSection(["HrisAdmin"]));
        Assert.Null(contributor.GetSection([]));
    }

    // -----------------------------------------------------------------------
    // TC-DASH-009 — OperationsAdminNavContributor: OpsNav target; null for
    //               non-OperationsAdmin roles.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_DASH_009_OperationsAdminNavContributor_CorrectTargetAndRoleFilter()
    {
        var contributor = new OperationsAdminNavContributor();

        Assert.Equal(NavTarget.OpsNav, contributor.Target);

        var opsSection = contributor.GetSection(["OperationsAdmin"]);
        Assert.NotNull(opsSection);
        Assert.NotEmpty(opsSection!.Items);

        Assert.Null(contributor.GetSection(["SystemAdmin"]));
        Assert.Null(contributor.GetSection(["PayrollAdmin"]));
    }

    // -----------------------------------------------------------------------
    // TC-DASH-010 — All six INavContributor implementations exist, declare the
    //               correct NavTarget, and return non-empty sections for their
    //               required roles.
    // -----------------------------------------------------------------------
    [Fact]
    public void TC_DASH_010_AllNavContributorsExistAndReturnCorrectSections()
    {
        var contributors = new (INavContributor Contributor, string[] Roles, NavTarget ExpectedTarget)[]
        {
            (new HrisNavContributor(),           ["HrisViewer"],      NavTarget.EntityNav),
            (new PayrollNavContributor(),         ["PayrollOperator"], NavTarget.EntityNav),
            (new BenefitsNavContributor(),        ["BenefitsAdmin"],   NavTarget.EntityNav),
            (new TaxNavContributor(),             ["TaxAdmin"],        NavTarget.EntityNav),
            (new SystemAdminNavContributor(),     ["SystemAdmin"],     NavTarget.AdminNav),
            (new OperationsAdminNavContributor(), ["OperationsAdmin"], NavTarget.OpsNav),
        };

        foreach (var (contributor, roles, expectedTarget) in contributors)
        {
            Assert.Equal(expectedTarget, contributor.Target);

            var navSection = contributor.GetSection(roles);
            Assert.NotNull(navSection);
            Assert.NotEmpty(navSection!.Items);
        }
    }
}

// Stand-in for tests that construct OrgStructureService but never call CreateOrgUnitAsync.
file sealed class NullTaxProfileRepository : ITaxProfileRepository
{
    public Task<IReadOnlyList<TaxJurisdictionRow>>         GetAllJurisdictionsAsync()                                                                                => Task.FromResult<IReadOnlyList<TaxJurisdictionRow>>([]);
    public Task<IReadOnlyList<TaxJurisdictionRow>>         GetJurisdictionsByLegalEntityAsync(Guid legalEntityId)                                                    => Task.FromResult<IReadOnlyList<TaxJurisdictionRow>>([]);
    public Task<IReadOnlyList<EmployeeJurisdictionRow>>    GetJurisdictionsByEmployeeAsync(Guid legalEntityId, Guid employmentId, DateOnly operativeDate, int lookAheadDays = 60) => Task.FromResult<IReadOnlyList<EmployeeJurisdictionRow>>([]);
    public Task<IReadOnlyList<TaxFilingStatusRow>>         GetFilingStatusesAsync(string jurisdictionCode)                                                           => Task.FromResult<IReadOnlyList<TaxFilingStatusRow>>([]);
    public Task<IReadOnlyList<MissingElectionRow>>         GetEmployeesMissingElectionsAsync(Guid legalEntityId, DateOnly operativeDate, int page, int pageSize)     => Task.FromResult<IReadOnlyList<MissingElectionRow>>([]);
    public Task<TaxProfileRow?>                            GetActiveProfileAsync(Guid employmentId, string jurisdictionCode, DateOnly asOfDate)                      => Task.FromResult<TaxProfileRow?>(null);
    public Task                                            SaveProfileAsync(Guid employmentId, string jurisdictionCode, TaxProfileSaveModel model, string createdBy, DateOnly effectiveFrom) => Task.CompletedTask;
    public Task                                            AssignJurisdictionsAsync(Guid legalEntityId, IEnumerable<string> jurisdictionCodes)                       => Task.CompletedTask;
}
