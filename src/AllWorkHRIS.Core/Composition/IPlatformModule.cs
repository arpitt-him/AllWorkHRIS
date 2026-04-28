// AllWorkHRIS.Core/Composition/IPlatformModule.cs
using Autofac;

namespace AllWorkHRIS.Core.Composition;

public interface IPlatformModule
{
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
