// AllWorkHRIS.Host/Config/Navigation/OperationsAdminNavContributor.cs
using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Host.Config.Navigation;

public sealed class OperationsAdminNavContributor : INavContributor
{
    public NavTarget Target => NavTarget.OpsNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        if (!userRoles.Contains("OperationsAdmin")) return null;

        return new NavSection(
            Label:      "Operations",
            Order:      0,
            BadgeLabel: null,
            AccentColor: null,
            Items:
            [
                new("Console", "/ops"),
            ]);
    }
}
