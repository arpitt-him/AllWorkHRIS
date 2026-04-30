using System.Composition;
using System.Threading.Channels;
using Autofac;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Payroll.Domain.Events;
using AllWorkHRIS.Module.Payroll.Jobs;
using AllWorkHRIS.Module.Payroll.Repositories;
using AllWorkHRIS.Module.Payroll.Services;
using Microsoft.Extensions.Hosting;

namespace AllWorkHRIS.Module.Payroll;

[Export(typeof(IPlatformModule))]
public sealed class PayrollModule : IPlatformModule
{
    public string  ModuleName        => "Payroll";
    public string  ModuleVersion     => "0.1.0";
    public string? ModuleDescription => "Payroll run lifecycle, calculation engine, accumulators, and pay register.";

    public void Register(ContainerBuilder builder)
    {
        // Channel<Guid> for the run job queue — singleton so the hosted service
        // and the run service share the same writer/reader pair
        builder.RegisterInstance(Channel.CreateUnbounded<Guid>())
               .SingleInstance();

        // Repositories
        builder.RegisterType<PayrollProfileRepository>()
               .As<IPayrollProfileRepository>()
               .InstancePerLifetimeScope();
        builder.RegisterType<PayrollRunRepository>()
               .As<IPayrollRunRepository>()
               .InstancePerLifetimeScope();
        builder.RegisterType<PayrollRunResultSetRepository>()
               .As<IPayrollRunResultSetRepository>()
               .InstancePerLifetimeScope();
        builder.RegisterType<EmployeePayrollResultRepository>()
               .As<IEmployeePayrollResultRepository>()
               .InstancePerLifetimeScope();
        builder.RegisterType<ResultLineRepository>()
               .As<IResultLineRepository>()
               .InstancePerLifetimeScope();
        builder.RegisterType<AccumulatorRepository>()
               .As<IAccumulatorRepository>()
               .InstancePerLifetimeScope();
        builder.RegisterType<PayrollContextRepository>()
               .As<IPayrollContextRepository>()
               .InstancePerLifetimeScope();

        // Services
        builder.RegisterType<PayrollRunService>().As<IPayrollRunService>().InstancePerLifetimeScope();
        builder.RegisterType<CalculationEngine>().As<ICalculationEngine>().InstancePerLifetimeScope();
        builder.RegisterType<AccumulatorService>().As<IAccumulatorService>().InstancePerLifetimeScope();
        builder.RegisterType<PayrollContextLookup>().As<IPayrollContextLookup>().InstancePerLifetimeScope();

        // Background job — singleton; resolves scoped services via ILifetimeScope child scope
        builder.RegisterType<PayrollRunJob>()
               .As<IHostedService>()
               .SingleInstance();

        // HRIS event handler singletons
        builder.RegisterType<HireEventHandler>().SingleInstance();
        builder.RegisterType<TerminationEventHandler>().SingleInstance();
        builder.RegisterType<CompensationChangeHandler>().SingleInstance();
        builder.RegisterType<LeaveApprovedHandler>().SingleInstance();

        // HRIS event subscriptions — register and wire handlers after container build
        // This is called after the bus is resolved by the host; see Phase 4.3 notes
        builder.RegisterType<PayrollEventSubscriber>()
               .As<IPayrollEventSubscriber>()
               .As<IEventSubscriber>()
               .SingleInstance();
    }

    public IEnumerable<MenuContribution> GetMenuContributions()
    {
        yield return new MenuContribution
        {
            Label        = "Payroll",
            Href         = null,
            Icon         = "PayrollIcon",
            SortOrder    = 20,
            RequiredRole = "PayrollOperator,PayrollAdmin",
            BadgeLabel   = "PAY",
            AccentColor  = "var(--module-payroll)"
        };
        yield return new MenuContribution
        {
            Label        = "Payroll Runs",
            Href         = "/payroll/runs",
            Icon         = "PayrollIcon",
            SortOrder    = 1,
            RequiredRole = "PayrollOperator,PayrollAdmin",
            ParentLabel  = "Payroll"
        };
        yield return new MenuContribution
        {
            Label        = "Pay Register",
            Href         = "/payroll/register",
            Icon         = "PayrollIcon",
            SortOrder    = 2,
            RequiredRole = "PayrollOperator,PayrollAdmin",
            ParentLabel  = "Payroll"
        };
        yield return new MenuContribution
        {
            Label        = "Accumulators",
            Href         = "/payroll/accumulators",
            Icon         = "PayrollIcon",
            SortOrder    = 3,
            RequiredRole = "PayrollAdmin",
            ParentLabel  = "Payroll"
        };
        yield return new MenuContribution
        {
            Label        = "Pay Calendars",
            Href         = "/payroll/calendar",
            Icon         = "PayrollIcon",
            SortOrder    = 4,
            RequiredRole = "PayrollAdmin",
            ParentLabel  = "Payroll"
        };
        yield return new MenuContribution
        {
            Label        = "Payroll Profiles",
            Href         = "/payroll/profiles",
            Icon         = "PayrollIcon",
            SortOrder    = 5,
            RequiredRole = "PayrollAdmin",
            ParentLabel  = "Payroll"
        };
    }
}
