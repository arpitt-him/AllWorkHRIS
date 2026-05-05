// AllWorkHRIS.Host/Config/Navigation/TaxNavContributor.cs
using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Host.Config.Navigation;

public sealed class TaxNavContributor : INavContributor
{
    private static readonly string[] _roles = ["TaxAdmin", "PayrollAdmin", "PayrollOperator"];

    public NavTarget Target => NavTarget.EntityNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        if (!userRoles.Any(r => _roles.Contains(r))) return null;

        return new NavSection(
            Label:      "Tax Setup",
            Order:      30,
            BadgeLabel: "TAX",
            AccentColor: "var(--module-tax, #b45309)",
            Items:
            [
                new("Tax Profiles",    "/payroll/tax-profiles"),
            ]);
    }
}
