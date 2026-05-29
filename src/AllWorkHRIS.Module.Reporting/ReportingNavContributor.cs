using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Module.Reporting;

/// <summary>
/// Sidebar contribution for the Reports section. M1 surfaces only the Payroll
/// Reports hub; HR Reports and Scheduled Reports are deferred until those
/// surfaces land.
/// </summary>
public sealed class ReportingNavContributor : INavContributor
{
    // Any role that can reach at least one report in the section. ReportViewer
    // is the spec's intended top-level gate but the role isn't seeded yet;
    // when it is, drop the others.
    // Section gate: ReportViewer is the base prerequisite for ANY visibility
    // into the Reports section, per SPEC/Reporting_Minimum_Module §14. Item-
    // level RequiredRole values below remain — but the user must hold
    // ReportViewer first to see the section at all.
    public NavTarget Target => NavTarget.EntityNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        if (!userRoles.Contains("ReportViewer")) return null;

        return new NavSection(
            Label:       "Reports",
            Order:       40,
            BadgeLabel:  "RPT",
            AccentColor: "var(--module-reporting)",
            Items:
            [
                new("Payroll Reports", "/reports/payroll", RequiredRole: "PayrollOperator,PayrollAdmin,Finance,TaxAdmin,Auditor"),
                new("HR Reports",      "/reports/hr",      RequiredRole: "HrisAdmin,HrisViewer,Manager,Finance,Auditor"),
            ]);
    }
}
