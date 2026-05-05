// AllWorkHRIS.Module.Payroll/PayrollNavContributor.cs
using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Module.Payroll;

public sealed class PayrollNavContributor : INavContributor
{
    private static readonly string[] _roles = ["PayrollOperator", "PayrollAdmin"];

    public NavTarget Target => NavTarget.EntityNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        if (!userRoles.Any(r => _roles.Contains(r))) return null;

        return new NavSection(
            Label:      "Payroll",
            Order:      20,
            BadgeLabel: "PAY",
            AccentColor: "var(--module-payroll)",
            Items:
            [
                new("Payroll Runs",      "/payroll/runs"),
                new("Pay Register",      "/payroll/register"),
                new("Accumulators",      "/payroll/accumulators",  RequiredRole: "PayrollAdmin"),
                new("Pay Calendars",     "/payroll/calendar",      RequiredRole: "PayrollAdmin"),
                new("Payroll Profiles",  "/payroll/profiles",      RequiredRole: "PayrollAdmin"),
            ]);
    }
}
