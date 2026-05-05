// AllWorkHRIS.Core/Navigation/INavContributor.cs
namespace AllWorkHRIS.Core.Navigation;

public interface INavContributor
{
    NavTarget Target { get; }

    /// <summary>
    /// Returns the nav section for this contributor, or null if the user
    /// does not hold any of the required roles.
    /// </summary>
    NavSection? GetSection(IEnumerable<string> userRoles);
}

/// <summary>
/// Which nav surface this contributor feeds into.
/// </summary>
public enum NavTarget
{
    EntityNav,
    AdminNav,
    OpsNav,
}

public sealed record NavSection(
    string                    Label,
    int                       Order,
    string?                   BadgeLabel,
    string?                   AccentColor,
    IReadOnlyList<NavSectionItem> Items);

public sealed record NavSectionItem(
    string  Label,
    string  Href,
    string? RequiredRole = null);
