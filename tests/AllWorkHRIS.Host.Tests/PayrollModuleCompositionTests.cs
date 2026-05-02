using System.Composition.Hosting;
using System.Threading.Channels;
using Autofac;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Payroll;
using AllWorkHRIS.Module.Payroll.Repositories;
using AllWorkHRIS.Module.Payroll.Services;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Composition tests for the Payroll plug-in module.
///
/// These tests verify the module's contract with the host shell:
///   1. MEF discovers the module from its assembly
///   2. Autofac wiring registers all expected service interfaces
///   3. Menu contributions match the declared structure
///   4. Module metadata is populated
///
/// No database connection is required — these are pure DI/composition tests.
/// </summary>
public sealed class PayrollModuleCompositionTests
{
    // -------------------------------------------------------
    // 1. MEF discovery
    // -------------------------------------------------------

    [Fact]
    public void PayrollModule_IsDiscoveredByMef_AsIPlatformModule()
    {
        // Arrange — scan the Payroll module assembly exactly as ModuleDiscovery does at runtime
        var config = new ContainerConfiguration()
            .WithAssembly(typeof(PayrollModule).Assembly);

        using var container = config.CreateContainer();

        // Act
        var modules = container.GetExports<IPlatformModule>().ToList();

        // Assert — exactly one export, and it is a PayrollModule
        Assert.Single(modules);
        Assert.IsType<PayrollModule>(modules[0]);
    }

    // -------------------------------------------------------
    // 2. Autofac container composition
    // -------------------------------------------------------

    [Fact]
    public void PayrollModule_Register_WiresAllExpectedServiceInterfaces()
    {
        // Arrange — satisfy the two host-level dependencies the module expects to find
        // already in the container (IConnectionFactory and Channel<Guid>).
        // We use a no-op fake for IConnectionFactory; Channel is the real type.
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new FakeConnectionFactory())
               .As<IConnectionFactory>()
               .SingleInstance();
        builder.RegisterInstance(Channel.CreateUnbounded<Guid>())
               .SingleInstance();

        new PayrollModule().Register(builder);
        using var container = builder.Build();

        // Assert — every service interface declared in the Payroll module is registered
        Assert.True(container.IsRegistered<IPayrollProfileRepository>(),      "IPayrollProfileRepository");
        Assert.True(container.IsRegistered<IPayrollRunRepository>(),          "IPayrollRunRepository");
        Assert.True(container.IsRegistered<IPayrollRunResultSetRepository>(), "IPayrollRunResultSetRepository");
        Assert.True(container.IsRegistered<IEmployeePayrollResultRepository>(),"IEmployeePayrollResultRepository");
        Assert.True(container.IsRegistered<IResultLineRepository>(),          "IResultLineRepository");
        Assert.True(container.IsRegistered<IAccumulatorRepository>(),         "IAccumulatorRepository");
        Assert.True(container.IsRegistered<IPayrollContextRepository>(),      "IPayrollContextRepository");
        Assert.True(container.IsRegistered<ICalculationEngine>(),             "ICalculationEngine");
        Assert.True(container.IsRegistered<IAccumulatorService>(),            "IAccumulatorService");
        Assert.True(container.IsRegistered<IPayrollEventSubscriber>(),        "IPayrollEventSubscriber");
    }

    // -------------------------------------------------------
    // 3. Menu contributions
    // -------------------------------------------------------

    [Fact]
    public void PayrollModule_GetMenuContributions_ReturnsExpectedItems()
    {
        var module        = new PayrollModule();
        var contributions = module.GetMenuContributions().ToList();

        // Six items: one parent + five children
        Assert.Equal(6, contributions.Count);

        // Parent — no href (it is a group header)
        Assert.Contains(contributions, c =>
            c.Label == "Payroll" &&
            c.Href  == null);

        // Child pages
        Assert.Contains(contributions, c =>
            c.Label == "Payroll Runs" &&
            c.Href  == "/payroll/runs");

        Assert.Contains(contributions, c =>
            c.Label == "Pay Register" &&
            c.Href  == "/payroll/register");

        Assert.Contains(contributions, c =>
            c.Label == "Accumulators" &&
            c.Href  == "/payroll/accumulators" &&
            c.RequiredRole == "PayrollAdmin");

        Assert.Contains(contributions, c =>
            c.Label == "Pay Calendars" &&
            c.Href  == "/payroll/calendar" &&
            c.RequiredRole == "PayrollAdmin");

        Assert.Contains(contributions, c =>
            c.Label == "Payroll Profiles" &&
            c.Href  == "/payroll/profiles" &&
            c.RequiredRole == "PayrollAdmin");
    }

    // -------------------------------------------------------
    // 4. Module metadata (IPlatformModule extension)
    // -------------------------------------------------------

    [Fact]
    public void PayrollModule_HasCorrectMetadata()
    {
        IPlatformModule module = new PayrollModule();

        Assert.Equal("Payroll", module.ModuleName);
        Assert.Equal("0.1.0",   module.ModuleVersion);
        Assert.False(string.IsNullOrWhiteSpace(module.ModuleDescription));
    }

    // -------------------------------------------------------
    // Helper — no-op IConnectionFactory (never called during
    // IsRegistered checks; Autofac registers types lazily)
    // -------------------------------------------------------

    private sealed class FakeConnectionFactory : IConnectionFactory
    {
        public System.Data.IDbConnection CreateConnection()
            => throw new NotSupportedException("FakeConnectionFactory is not for DB use.");
    }
}
