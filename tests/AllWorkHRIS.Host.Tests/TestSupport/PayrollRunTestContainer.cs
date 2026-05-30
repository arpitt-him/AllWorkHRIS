using Autofac;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Payroll.Repositories;
using AllWorkHRIS.Module.Payroll.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AllWorkHRIS.Host.Tests.TestSupport;

/// <summary>
/// Single source of truth for the Autofac container needed to drive
/// <c>PayrollRunJob</c> end-to-end in tests. Mirrors the Host's payroll
/// composition — real repositories + <c>CalculationEngine</c> + <c>AccumulatorService</c>
/// — with test doubles for the collaborators the engine requires but a
/// payroll-only test does not load real modules for:
///
///   - <see cref="IBenefitStepProvider"/>      (Benefits module) → <see cref="NoOpBenefitStepProvider"/>
///   - <see cref="IPayrollHoursSource"/>        (T&amp;A module)      → <see cref="StandardHoursPayrollHoursSource"/>
///   - <see cref="IPayrollPipelineService"/>    (Tax module)      → Core <c>NullPayrollPipelineService</c>
///   - <see cref="IEmploymentJurisdictionLookup"/> (Tax/HRIS)     → Core <c>NullEmploymentJurisdictionLookup</c>
///
/// Keeping this list in one place is the fix for ToDo #36: the engine and
/// <c>PayrollRunJob</c> accreted cross-module / infrastructure constructor
/// dependencies (IBenefitStepProvider, IPayrollHoursSource, IWallClock,
/// ILookupCache, IEarningsCodeRepository) over several phases while a hand-rolled
/// per-test container silently drifted out of date. Resolve everything the job
/// actually pulls here so a future added dependency fails one obvious place.
/// </summary>
internal static class PayrollRunTestContainer
{
    public static IContainer Build(IConnectionFactory connectionFactory, ILookupCache lookupCache)
    {
        var builder = new ContainerBuilder();

        // Infrastructure
        builder.RegisterInstance(connectionFactory).As<IConnectionFactory>().SingleInstance();
        builder.RegisterInstance(lookupCache).As<ILookupCache>().SingleInstance();
        builder.RegisterInstance((IAuditService)new NullAuditService()).As<IAuditService>().SingleInstance();
        builder.RegisterInstance((ITemporalContext)new SystemTemporalContext()).As<ITemporalContext>().SingleInstance();
        builder.RegisterType<WallClock>().As<IWallClock>().SingleInstance();
        builder.RegisterInstance(NullLogger<CalculationEngine>.Instance).As<ILogger<CalculationEngine>>().SingleInstance();

        // Cross-module collaborators — test doubles (see class summary)
        builder.RegisterInstance(new NullPayrollPipelineService()).As<IPayrollPipelineService>().SingleInstance();
        builder.RegisterInstance(new NullEmploymentJurisdictionLookup()).As<IEmploymentJurisdictionLookup>().SingleInstance();
        builder.RegisterInstance(new NoOpBenefitStepProvider()).As<IBenefitStepProvider>().SingleInstance();
        builder.RegisterInstance(new StandardHoursPayrollHoursSource()).As<IPayrollHoursSource>().SingleInstance();

        // Repositories
        builder.RegisterType<PayrollRunRepository>().As<IPayrollRunRepository>().InstancePerLifetimeScope();
        builder.RegisterType<PayrollRunResultSetRepository>().As<IPayrollRunResultSetRepository>().InstancePerLifetimeScope();
        builder.RegisterType<EmployeePayrollResultRepository>().As<IEmployeePayrollResultRepository>().InstancePerLifetimeScope();
        builder.RegisterType<PayrollProfileRepository>().As<IPayrollProfileRepository>().InstancePerLifetimeScope();
        builder.RegisterType<PayrollContextRepository>().As<IPayrollContextRepository>().InstancePerLifetimeScope();
        builder.RegisterType<PayrollCompensationSnapshotRepository>().As<IPayrollCompensationSnapshotRepository>().InstancePerLifetimeScope();
        builder.RegisterType<ResultLineRepository>().As<IResultLineRepository>().InstancePerLifetimeScope();
        builder.RegisterType<AccumulatorRepository>().As<IAccumulatorRepository>().InstancePerLifetimeScope();
        builder.RegisterType<EarningsCodeRepository>().As<IEarningsCodeRepository>().InstancePerLifetimeScope();

        // Services
        builder.RegisterType<CalculationEngine>().As<ICalculationEngine>().InstancePerLifetimeScope();
        builder.RegisterType<AccumulatorService>().As<IAccumulatorService>().InstancePerLifetimeScope();

        return builder.Build();
    }
}
