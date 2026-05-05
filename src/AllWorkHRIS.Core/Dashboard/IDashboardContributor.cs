// AllWorkHRIS.Core/Dashboard/IDashboardContributor.cs
namespace AllWorkHRIS.Core.Dashboard;

public interface IDashboardContributor
{
    string ModuleName { get; }
    string AccentColor { get; }

    /// <summary>
    /// Roles that qualify a user to see this contributor's items.
    /// The dashboard calls this contributor only when the user holds at least one.
    /// Empty means all authenticated users see these items.
    /// </summary>
    IReadOnlyList<string> RequiredRoles { get; }

    /// <summary>
    /// Returns outstanding, upcoming, or pending items for the given entity (or all
    /// accessible entities when entityId is null). Implementations must not throw —
    /// return an empty list on query failure.
    /// </summary>
    Task<IReadOnlyList<DashboardItem>> GetItemsAsync(
        Guid?               entityId,
        IEnumerable<string> userRoles,
        CancellationToken   ct = default);
}

public sealed record DashboardItem(
    string              Title,
    string?             Subtitle,
    Guid                EntityId,
    string              EntityName,
    string              Route,
    DashboardItemUrgency Urgency,
    string              ModuleName,
    string              AccentColor);

public enum DashboardItemUrgency
{
    Info      = 0,
    Upcoming  = 1,
    Attention = 2,
    Urgent    = 3,
}
