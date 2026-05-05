using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Host.Components.Pages.Hris;

namespace AllWorkHRIS.Host.Hris.TabContributors;

public sealed class BenefitsTabContributor : IEmployeeTabContributor
{
    public string  Label         => "Benefits";
    public int     SortOrder     => 80;
    public string? RequiredRole  => null;
    public Type    ComponentType => typeof(EmployeeBenefitsTab);
}
