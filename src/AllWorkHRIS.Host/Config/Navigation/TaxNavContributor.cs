// AllWorkHRIS.Host/Config/Navigation/TaxNavContributor.cs
using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Host.Config.Navigation;

public sealed class TaxNavContributor : INavContributor
{
    public NavTarget Target => NavTarget.EntityNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        var roles = userRoles.ToHashSet();

        var isTaxAdmin          = roles.Contains("TaxAdmin");
        var isComplianceReviewer= roles.Contains("ComplianceReviewer");
        var isPayrollAdmin      = roles.Contains("PayrollAdmin");
        var isPayrollOperator   = roles.Contains("PayrollOperator");

        if (!isTaxAdmin && !isComplianceReviewer && !isPayrollAdmin && !isPayrollOperator)
            return null;

        var items = new List<NavSectionItem>();

        // Entity-level operations — not visible to ComplianceReviewer
        if (isTaxAdmin || isPayrollAdmin || isPayrollOperator)
            items.Add(new("Tax Profiles", "/payroll/tax-profiles"));

        if (isTaxAdmin || isPayrollAdmin)
            items.Add(new("Jurisdictions", "/payroll/tax-setup/jurisdictions"));

        if (isTaxAdmin || isPayrollAdmin)
            items.Add(new("Rate Tables", "/payroll/tax/rates"));

        // Tax configuration — TaxAdmin, ComplianceReviewer, PayrollAdmin
        if (isTaxAdmin || isComplianceReviewer || isPayrollAdmin)
        {
            items.Add(new("Calculation Steps",  "/config/tax/steps"));
            items.Add(new("Form Fields",         "/config/tax/form-fields"));
            items.Add(new("Review & Approve",    "/config/tax/review"));
            items.Add(new("Preview Sandbox",     "/config/tax/preview"));
        }

        return new NavSection(
            Label:      "Tax Setup",
            Order:      30,
            BadgeLabel: "TAX",
            AccentColor: "var(--module-tax, #b45309)",
            Items:      [.. items]);
    }
}
