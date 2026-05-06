using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Module.TimeAttendance.Pages;

namespace AllWorkHRIS.Module.TimeAttendance.TabContributors;

public sealed class TimeTabContributor : IEmployeeTabContributor
{
    public string  Label         => "Time";
    public int     SortOrder     => 85;
    public string? RequiredRole  => null;
    public Type    ComponentType => typeof(EmployeeTimeTab);
}
