// AllWorkHRIS.Module.Benefits/BenefitsNavContributor.cs
using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Module.Benefits;

public sealed class BenefitsNavContributor : INavContributor
{
    private static readonly string[] _roles = ["BenefitsAdmin", "HrisAdmin", "PayrollOperator"];

    public NavTarget Target => NavTarget.EntityNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        if (!userRoles.Any(r => _roles.Contains(r))) return null;

        return new NavSection(
            Label:      "Benefits",
            Order:      25,
            BadgeLabel: "BEN",
            AccentColor: "var(--module-benefits, #047857)",
            Items:
            [
                new("Deduction Codes",  "/benefits/codes",    RequiredRole: "BenefitsAdmin"),
                new("Elections",        "/benefits/elections"),
                new("Import Elections", "/benefits/import",   RequiredRole: "BenefitsAdmin"),
            ]);
    }
}
