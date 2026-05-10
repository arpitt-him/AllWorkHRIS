// AllWorkHRIS.Host/Hris/Navigation/HrisNavContributor.cs
using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Host.Hris.Navigation;

public sealed class HrisNavContributor : INavContributor
{
    private static readonly string[] _roles = ["HrisViewer", "HrisAdmin", "HrisOperator", "Manager"];

    public NavTarget Target => NavTarget.EntityNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        if (!userRoles.Any(r => _roles.Contains(r))) return null;

        return new NavSection(
            Label:      "People",
            Order:      10,
            BadgeLabel: "HR",
            AccentColor: "var(--module-hris, #1d4ed8)",
            Items:
            [
                new("Employees",          "/hris/employees"),
                new("Organization",       "/hris/org"),
                new("Jobs & Positions",   "/hris/jobs"),
                new("Work Queue",         "/hris/workqueue",        RequiredRole: "HrisAdmin"),
                new("Document Expiry",    "/hris/documents/expiring", RequiredRole: "HrisAdmin"),
            ]);
    }
}
