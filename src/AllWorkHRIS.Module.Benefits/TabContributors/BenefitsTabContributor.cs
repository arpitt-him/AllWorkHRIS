using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Module.Benefits.Pages;

namespace AllWorkHRIS.Module.Benefits.TabContributors;

public sealed class BenefitsTabContributor : IEmployeeTabContributor
{
    public string  Label         => "Benefits";
    public int     SortOrder     => 80;
    public string? RequiredRole  => null;
    public Type    ComponentType => typeof(EmployeeBenefitsTab);
}
