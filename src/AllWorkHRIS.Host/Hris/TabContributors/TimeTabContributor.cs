using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Host.Components.Pages.Hris;

namespace AllWorkHRIS.Host.Hris.TabContributors;

public sealed class TimeTabContributor : IEmployeeTabContributor
{
    public string  Label         => "Time";
    public int     SortOrder     => 85;
    public string? RequiredRole  => null;
    public Type    ComponentType => typeof(EmployeeTimeTab);
}
