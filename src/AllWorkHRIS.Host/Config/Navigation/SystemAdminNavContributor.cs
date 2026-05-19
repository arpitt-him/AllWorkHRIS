// AllWorkHRIS.Host/Config/Navigation/SystemAdminNavContributor.cs
using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Host.Config.Navigation;

public sealed class SystemAdminNavContributor : INavContributor
{
    public NavTarget Target => NavTarget.AdminNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        if (!userRoles.Contains("SystemAdmin")) return null;

        return new NavSection(
            Label:      "Administration",
            Order:      0,
            BadgeLabel: null,
            AccentColor: null,
            Items:
            [
                new("Dashboard",             "/"),
                new("Legal Entities",        "/config/legal-entities"),
                new("Tax Rate Tables",        "/payroll/tax/rates"),
                new("Rate Reference",         "/config/tax/reference"),
                new("Calculation Steps",      "/config/tax/steps"),
                new("Tax Review",            "/config/tax/review"),
                new("Form Definitions",      "/config/tax/form-fields"),
                new("Preview Sandbox",       "/config/tax/preview"),
                new("System Settings",       "/about"),
            ]);
    }
}
