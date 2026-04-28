// AllWorkHRIS.Core/Composition/MenuContribution.cs
namespace AllWorkHRIS.Core.Composition;

public sealed class MenuContribution
{
    /// <summary>Display label for the menu item.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Navigation href. Null for parent/group items that have no
    /// direct navigation target.
    /// </summary>
    public string? Href { get; init; }

    /// <summary>Icon identifier. Resolves to a Razor component name.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Sort order within the menu. Lower numbers appear first.
    /// Host-owned items use 0–9. Module items start at 10.
    /// </summary>
    public int SortOrder { get; init; }

    /// <summary>
    /// Role required to see this item.
    /// Null means visible to all authenticated users.
    /// </summary>
    public string? RequiredRole { get; init; }

    /// <summary>
    /// Label of the parent menu group.
    /// Null means this is a top-level item.
    /// </summary>
    public string? ParentLabel { get; init; }

    /// <summary>
    /// Accent color for the module badge on the menu item.
    /// CSS color value e.g. "var(--module-teal)".
    /// </summary>
    public string? AccentColor { get; init; }

    /// <summary>
    /// Short badge label displayed on the menu item
    /// e.g. "HRIS", "PAY", "T&amp;A".
    /// </summary>
    public string? BadgeLabel { get; init; }
}
