using System.Composition;
using Autofac;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Dashboard;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Navigation;
using AllWorkHRIS.Module.TimeAttendance.Events;
using AllWorkHRIS.Module.TimeAttendance.Repositories;
using AllWorkHRIS.Module.TimeAttendance.Services;

namespace AllWorkHRIS.Module.TimeAttendance;

[Export(typeof(IPlatformModule))]
public sealed class TimeAttendanceModule : IPlatformModule
{
    public string  ModuleName        => "TimeAttendance";
    public string  ModuleVersion     => "0.1.0";
    public string? ModuleDescription => "Time entry capture, approval, overtime detection, and payroll handoff.";

    public void Register(ContainerBuilder builder)
    {
        builder.RegisterType<TimeEntryRepository>()
               .As<ITimeEntryRepository>()
               .InstancePerLifetimeScope();

        builder.RegisterType<WorkScheduleRepository>()
               .As<IWorkScheduleRepository>()
               .InstancePerLifetimeScope();

        builder.RegisterType<OvertimeDetectionService>()
               .As<IOvertimeDetectionService>()
               .InstancePerLifetimeScope();

        builder.RegisterType<TimeEntryService>()
               .As<ITimeEntryService>()
               .InstancePerLifetimeScope();

        builder.RegisterType<PayrollHandoffService>()
               .As<IPayrollHandoffService>()
               .InstancePerLifetimeScope();

        builder.RegisterType<TimeImportService>()
               .As<ITimeImportService>()
               .InstancePerLifetimeScope();

        // Null notifier — overridden by Host with WorkQueueTimeApprovalNotifier
        builder.RegisterType<NullTimeApprovalNotifier>()
               .As<ITimeApprovalNotifier>()
               .SingleInstance()
               .IfNotRegistered(typeof(ITimeApprovalNotifier));

        builder.RegisterType<TimeAttendanceDashboardContributor>()
               .As<IDashboardContributor>()
               .InstancePerLifetimeScope();

        builder.RegisterType<TimeAttendanceNavContributor>()
               .As<INavContributor>()
               .SingleInstance();

        builder.RegisterType<TimeAttendanceEventSubscriber>()
               .As<TimeAttendanceEventSubscriber>()
               .As<IEventSubscriber>()
               .InstancePerLifetimeScope();
    }

    public IEnumerable<MenuContribution> GetMenuContributions()
    {
        yield return new MenuContribution
        {
            Label        = "Time & Attendance",
            Href         = null,
            Icon         = "TAIcon",
            SortOrder    = 25,
            RequiredRole = "TimeViewer,TimeAdmin,Manager,Employee",
            BadgeLabel   = "T&A",
            AccentColor  = "var(--module-ta, #7c3aed)"
        };
        yield return new MenuContribution
        {
            Label       = "Timecards",
            Href        = "/ta/timecards",
            SortOrder   = 1,
            RequiredRole = "TimeViewer,TimeAdmin,Manager",
            ParentLabel = "Time & Attendance"
        };
        yield return new MenuContribution
        {
            Label       = "My Timecard",
            Href        = "/ta/my-timecard",
            SortOrder   = 2,
            RequiredRole = "Employee",
            ParentLabel = "Time & Attendance"
        };
        yield return new MenuContribution
        {
            Label       = "Payroll Handoff",
            Href        = "/ta/handoff",
            SortOrder   = 3,
            RequiredRole = "TimeAdmin",
            ParentLabel = "Time & Attendance"
        };
        yield return new MenuContribution
        {
            Label        = "Import Entries",
            Href         = "/ta/import",
            SortOrder    = 4,
            RequiredRole = "TimeAdmin",
            ParentLabel  = "Time & Attendance"
        };
    }
}
