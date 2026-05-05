using System.Composition;
using Autofac;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Dashboard;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Navigation;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Events;
using AllWorkHRIS.Module.Benefits.Repositories;
using AllWorkHRIS.Module.Benefits.Services;
using AllWorkHRIS.Module.Benefits.Steps;
using AllWorkHRIS.Module.Benefits.Steps.Calculators;

namespace AllWorkHRIS.Module.Benefits;

[Export(typeof(IPlatformModule))]
public sealed class BenefitsModule : IPlatformModule
{
    public string  ModuleName        => "Benefits";
    public string  ModuleVersion     => "0.1.0";
    public string? ModuleDescription => "Benefit deduction elections and payroll delivery.";

    public void Register(ContainerBuilder builder)
    {
        builder.RegisterType<DeductionRepository>()
               .As<IDeductionRepository>()
               .InstancePerLifetimeScope();

        builder.RegisterType<BenefitElectionRepository>()
               .As<IBenefitElectionRepository>()
               .InstancePerLifetimeScope();

        builder.RegisterType<DeductionRateTableRepository>()
               .As<IDeductionRateTableRepository>()
               .InstancePerLifetimeScope();

        builder.RegisterType<DeductionEmployerMatchRepository>()
               .As<IDeductionEmployerMatchRepository>()
               .InstancePerLifetimeScope();

        builder.RegisterType<BenefitElectionService>()
               .As<IBenefitElectionService>()
               .InstancePerLifetimeScope();

        builder.RegisterType<BenefitElectionImportService>()
               .As<IBenefitElectionImportService>()
               .InstancePerLifetimeScope();

        // Calculator strategy — one instance per mode, shared across requests
        builder.RegisterType<FixedPerPeriodCalculator>().As<IBenefitCalculator>().SingleInstance();
        builder.RegisterType<FixedAnnualCalculator>()   .As<IBenefitCalculator>().SingleInstance();
        builder.RegisterType<FixedMonthlyCalculator>()  .As<IBenefitCalculator>().SingleInstance();
        builder.RegisterType<PctPreTaxCalculator>()     .As<IBenefitCalculator>().SingleInstance();
        builder.RegisterType<CoverageBasedCalculator>() .As<IBenefitCalculator>().SingleInstance();

        builder.RegisterType<BenefitCalculatorFactory>()
               .AsSelf()
               .SingleInstance();

        // IBenefitStepProvider — consumed by PayrollPipelineService during step assembly
        builder.RegisterType<BenefitStepProvider>()
               .As<IBenefitStepProvider>()
               .InstancePerLifetimeScope();

        // Dashboard contributor
        builder.RegisterType<BenefitsDashboardContributor>()
               .As<IDashboardContributor>()
               .InstancePerLifetimeScope();

        // Nav contributor
        builder.RegisterType<BenefitsNavContributor>()
               .As<INavContributor>()
               .SingleInstance();

        builder.RegisterType<BenefitsEventSubscriber>()
               .As<BenefitsEventSubscriber>()
               .As<IEventSubscriber>()
               .InstancePerLifetimeScope();
    }

    public IEnumerable<MenuContribution> GetMenuContributions()
    {
        yield return new MenuContribution
        {
            Label        = "Benefits",
            Href         = null,
            Icon         = "BenefitsIcon",
            SortOrder    = 25,
            RequiredRole = "HrisAdmin,BenefitsAdmin,PayrollOperator",
            BadgeLabel   = "BEN",
            AccentColor  = "var(--module-benefits, #047857)"
        };
        yield return new MenuContribution
        {
            Label        = "Deduction Codes",
            Href         = "/benefits/codes",
            Icon         = "BenefitsIcon",
            SortOrder    = 1,
            RequiredRole = "HrisAdmin,BenefitsAdmin",
            ParentLabel  = "Benefits"
        };
        yield return new MenuContribution
        {
            Label        = "Elections",
            Href         = "/benefits/elections",
            Icon         = "BenefitsIcon",
            SortOrder    = 2,
            RequiredRole = "HrisAdmin,BenefitsAdmin,PayrollOperator",
            ParentLabel  = "Benefits"
        };
        yield return new MenuContribution
        {
            Label        = "Import Elections",
            Href         = "/benefits/import",
            Icon         = "BenefitsIcon",
            SortOrder    = 3,
            RequiredRole = "HrisAdmin,BenefitsAdmin",
            ParentLabel  = "Benefits"
        };
    }
}
