using System.Composition;
using Autofac;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Navigation;
using AllWorkHRIS.Module.Reporting.Export;
using AllWorkHRIS.Module.Reporting.Jobs;
using AllWorkHRIS.Module.Reporting.Progress;
using AllWorkHRIS.Module.Reporting.Reports;
using AllWorkHRIS.Module.Reporting.Reports.Payroll;
using AllWorkHRIS.Module.Reporting.Repositories;
using AllWorkHRIS.Module.Reporting.Services;
using Microsoft.Extensions.Hosting;

namespace AllWorkHRIS.Module.Reporting;

[Export(typeof(IPlatformModule))]
public sealed class ReportingModule : IPlatformModule
{
    public string  ModuleName        => "Reporting";
    public string? ModuleDescription => "Pre-built operational reports for payroll and HR — view, CSV/XLSX/PDF export, execution history.";

    public void Register(ContainerBuilder builder)
    {
        // QuestPDF Community license declaration. Free for organisations under
        // their revenue threshold (AllWorkHRIS qualifies); no key or activation
        // — the library throws at first render without this. Set here so the
        // Host project doesn't need a direct QuestPDF reference.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        // Module-owned wrapper around the report-generation channel — typed
        // distinctly so it doesn't collide with Payroll's Channel<Guid>
        // registration in the shared container (Autofac is last-wins on
        // identical service types).
        builder.RegisterType<ReportJobQueue>()
               .AsSelf()
               .SingleInstance();

        // Repositories
        builder.RegisterType<ReportHistoryRepository>()
               .As<IReportHistoryRepository>()
               .InstancePerLifetimeScope();

        // Read-side helpers used by parameter panels
        builder.RegisterType<Queries.PayrollRunPickerQuery>()
               .AsSelf()
               .InstancePerLifetimeScope();

        // Reports (IReportQuery implementations — discovered into the registry)
        builder.RegisterType<PAY_RPT_001_PayrollRegister>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<PAY_RPT_002_GrossToNetSummary>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<PAY_RPT_003_EmployerCost>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<PAY_RPT_004_PayrollException>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<PAY_RPT_005_YtdAccumulatorBalance>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<PAY_RPT_006_PayrollVariance>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<PAY_RPT_007_PaymentDisbursement>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<PAY_RPT_008_TaxLiabilitySummary>()
               .As<IReportQuery>().InstancePerLifetimeScope();

        builder.RegisterType<Reports.HR.HR_RPT_001_ActiveHeadcount>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<Reports.HR.HR_RPT_002_NewHireTermination>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<Reports.HR.HR_RPT_003_Turnover>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<Reports.HR.HR_RPT_004_LeaveUtilisation>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<Reports.HR.HR_RPT_005_OpenPosition>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<Reports.HR.HR_RPT_006_CompensationSummary>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<Reports.HR.HR_RPT_007_OnboardingStatus>()
               .As<IReportQuery>().InstancePerLifetimeScope();
        builder.RegisterType<Reports.HR.HR_RPT_008_DocumentExpiration>()
               .As<IReportQuery>().InstancePerLifetimeScope();

        // Registry — composes every IReportQuery resolved above
        builder.RegisterType<ReportRegistry>()
               .As<IReportRegistry>()
               .InstancePerLifetimeScope();

        // Exporters
        builder.RegisterType<CsvExporter>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<XlsxExporter>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<PdfExporter>().AsSelf().InstancePerLifetimeScope();

        // Services
        builder.RegisterType<ReportService>()
               .As<IReportService>()
               .InstancePerLifetimeScope();
        builder.RegisterType<ReportExportService>()
               .As<IReportExportService>()
               .InstancePerLifetimeScope();

        // Async infrastructure — singleton state shared across requests
        builder.RegisterType<InMemoryReportProgressNotifier>()
               .As<IReportProgressNotifier>()
               .SingleInstance();
        builder.RegisterType<InMemoryReportResultCache>()
               .As<IReportResultCache>()
               .SingleInstance();

        // Background job — singleton; resolves scoped services via a child scope per execution.
        builder.RegisterType<ReportGenerationJob>()
               .As<IHostedService>()
               .SingleInstance();

        // Nav contributor
        builder.RegisterType<ReportingNavContributor>()
               .As<INavContributor>()
               .SingleInstance();
    }

    public IEnumerable<MenuContribution> GetMenuContributions()
    {
        // Header — gated to ReportViewer (the spec's base prerequisite).
        yield return new MenuContribution
        {
            Label        = "Reports",
            Href         = null,
            Icon         = "ReportingIcon",
            SortOrder    = 40,
            RequiredRole = "ReportViewer",
            BadgeLabel   = "RPT",
            AccentColor  = "var(--module-reporting)"
        };

        yield return new MenuContribution
        {
            Label        = "Payroll Reports",
            Href         = "/reports/payroll",
            Icon         = "ReportingIcon",
            SortOrder    = 1,
            RequiredRole = "ReportViewer",
            ParentLabel  = "Reports"
        };

        yield return new MenuContribution
        {
            Label        = "HR Reports",
            Href         = "/reports/hr",
            Icon         = "ReportingIcon",
            SortOrder    = 2,
            RequiredRole = "ReportViewer",
            ParentLabel  = "Reports"
        };
    }
}
