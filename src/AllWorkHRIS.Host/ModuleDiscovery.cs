// AllWorkHRIS.Host/ModuleDiscovery.cs
using System.Composition.Hosting;
using System.Runtime.Loader;
using AllWorkHRIS.Core.Composition;

namespace AllWorkHRIS.Host;

public static class ModuleDiscovery
{
    // enabledModuleNames: short names like "Payroll", "Benefits" (no "AllWorkHRIS.Module." prefix).
    // null  -> load every AllWorkHRIS.Module.*.dll found (back-compat default).
    // empty -> load none (HRIS-core only).
    // list  -> load only those listed.
    public static IReadOnlyList<IPlatformModule> DiscoverModules(
        string modulesPath,
        IReadOnlyCollection<string>? enabledModuleNames = null)
    {
        if (!Directory.Exists(modulesPath))
        {
            Console.WriteLine($"[ModuleDiscovery] Modules path '{modulesPath}' does not exist. No modules loaded.");
            return [];
        }

        var enabledSet = enabledModuleNames is null
            ? null
            : new HashSet<string>(enabledModuleNames, StringComparer.OrdinalIgnoreCase);

        if (enabledSet is not null)
            Console.WriteLine($"[ModuleDiscovery] Modules:Enabled filter active ({enabledSet.Count} entries): {string.Join(", ", enabledSet)}");

        var assemblies = Directory
            .GetFiles(modulesPath, "AllWorkHRIS.Module.*.dll")
            .Where(path =>
            {
                if (enabledSet is null) return true;
                var shortName = Path.GetFileNameWithoutExtension(path)
                                    .Substring("AllWorkHRIS.Module.".Length);
                var include = enabledSet.Contains(shortName);
                if (!include)
                    Console.WriteLine($"[ModuleDiscovery] Skipped (not enabled): {shortName}");
                return include;
            })
            .Select(path =>
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
                Console.WriteLine($"[ModuleDiscovery] Loaded assembly: {assembly.GetName().Name}");
                return assembly;
            })
            .ToList();

        if (assemblies.Count == 0)
        {
            Console.WriteLine("[ModuleDiscovery] No module assemblies loaded.");
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
