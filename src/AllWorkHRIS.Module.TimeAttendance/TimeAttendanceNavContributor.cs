using AllWorkHRIS.Core.Navigation;

namespace AllWorkHRIS.Module.TimeAttendance;

public sealed class TimeAttendanceNavContributor : INavContributor
{
    private static readonly string[] _roles = ["TimeViewer", "TimeAdmin", "Manager", "Employee"];

    public NavTarget Target => NavTarget.EntityNav;

    public NavSection? GetSection(IEnumerable<string> userRoles)
    {
        var roles = userRoles.ToList();
        if (!roles.Any(r => _roles.Contains(r))) return null;

        var items = new List<NavSectionItem>();

        if (roles.Any(r => r is "TimeViewer" or "TimeAdmin" or "Manager"))
            items.Add(new("Timecards", "/ta/timecards"));

        if (roles.Contains("Employee"))
            items.Add(new("My Timecard", "/ta/my-timecard"));

        if (roles.Any(r => r is "TimeAdmin"))
        {
            items.Add(new("Payroll Handoff", "/ta/handoff", RequiredRole: "TimeAdmin"));
            items.Add(new("Import Entries",  "/ta/import",  RequiredRole: "TimeAdmin"));
        }

        if (items.Count == 0) return null;

        return new NavSection(
            Label:       "Time Tracking",
            Order:       25,
            BadgeLabel:  "T&A",
            AccentColor: "var(--module-ta, #7c3aed)",
            Items:       items);
    }
}
