// AllWorkHRIS.Host/Config/Navigation/SystemAdminNavContributor.cs
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Host.Config.Navigation;

public sealed class SystemAdminNavContributor : INavContributor
{
    private const string TaxModuleAssembly = "AllWorkHRIS.Module.Tax";

    private readonly bool _taxModuleLoaded;

    public SystemAdminNavContributor(IReadOnlyList<IPlatformModule> modules)
    {
        _taxModuleLoaded = modules.Any(m =>
            string.Equals(m.GetType().Assembly.GetName().Name,
                          TaxModuleAssembly,
                          StringComparison.Ordinal));
    }

    public NavTarget Target => NavTarget.AdminNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        if (!userRoles.Contains("SystemAdmin")) return null;

        var items = new List<NavSectionItem>
        {
            new("Dashboard",      "/"),
            new("Legal Entities", "/config/legal-entities"),
        };

        // Tax administration items depend on the Tax module being composed
        // and its backing schema applied. Omit them entirely when the module
        // isn't loaded so SystemAdmin doesn't click into a broken page.
        if (_taxModuleLoaded)
        {
            items.Add(new("Tax Rate Tables",  "/payroll/tax/rates"));
            items.Add(new("Rate Reference",   "/config/tax/reference"));
            items.Add(new("Calculation Steps","/config/tax/steps"));
            items.Add(new("Tax Review",       "/config/tax/review"));
            items.Add(new("Form Definitions", "/config/tax/form-fields"));
            items.Add(new("Preview Sandbox",  "/config/tax/preview"));
        }

        items.Add(new("System Settings", "/about"));

        return new NavSection(
            Label:       "Administration",
            Order:       0,
            BadgeLabel:  null,
            AccentColor: null,
            Items:       [.. items]);
    }
}
