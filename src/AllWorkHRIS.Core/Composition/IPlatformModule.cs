// AllWorkHRIS.Core/Composition/IPlatformModule.cs
using Autofac;

namespace AllWorkHRIS.Core.Composition;

public interface IPlatformModule
{
    /// <summary>Human-readable module name shown in system UI (e.g. "Payroll").</summary>
    string ModuleName => GetType().Name.Replace("Module", "", StringComparison.Ordinal);

    /// <summary>Semantic version string (e.g. "0.1.0").</summary>
    string ModuleVersion => GetType().Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Optional one-line description shown on the About page.</summary>
    string? ModuleDescription => null;

    /// <summary>
    /// Registers all module services, repositories, and domain types
    /// into the Autofac container builder.
    /// Called once at startup before the container is built.
    /// Must be stateless — do not store any application state here.
    /// </summary>
    void Register(ContainerBuilder builder);

    /// <summary>
    /// Returns the navigation menu items this module contributes
    /// to the host application shell.
    /// Called once at startup to build the assembled menu singleton.
    /// </summary>
    IEnumerable<MenuContribution> GetMenuContributions();
}
