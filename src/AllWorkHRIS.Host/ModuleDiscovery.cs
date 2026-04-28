// AllWorkHRIS.Host/ModuleDiscovery.cs
using System.Composition.Hosting;
using System.Runtime.Loader;
using AllWorkHRIS.Core.Composition;

namespace AllWorkHRIS.Host;

public static class ModuleDiscovery
{
    public static IReadOnlyList<IPlatformModule> DiscoverModules(string modulesPath)
    {
        if (!Directory.Exists(modulesPath))
        {
            Console.WriteLine($"[ModuleDiscovery] Modules path '{modulesPath}' does not exist. No modules loaded.");
            return [];
        }

        var assemblies = Directory
            .GetFiles(modulesPath, "AllWorkHRIS.Module.*.dll")
            .Select(path =>
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                Console.WriteLine($"[ModuleDiscovery] Loaded assembly: {assembly.GetName().Name}");
                return assembly;
            })
            .ToList();

        if (assemblies.Count == 0)
        {
            Console.WriteLine("[ModuleDiscovery] No module assemblies found.");
            return [];
        }

        var configuration = new ContainerConfiguration()
            .WithAssemblies(assemblies);

        using var container = configuration.CreateContainer();

        var modules = container.GetExports<IPlatformModule>().ToList();

        foreach (var module in modules)
            Console.WriteLine($"[ModuleDiscovery] Registered module: {module.GetType().FullName}");

        return modules;
    }
}
