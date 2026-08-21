using System.Reflection;
using HrSuite.Core.Modularity;

namespace HrSuite.API.Modularity;

/// <summary>
/// Startup discovery for layers 3, 4 and 5. The host knows the folder convention and the
/// IPluginModule contract - nothing else. Adding an add-on means dropping a folder under
/// plugins\; it never means editing base code.
/// </summary>
public static class PluginLoader
{
    public const string PluginFolderName = "plugins";

    public static IReadOnlyList<LoadedPlugin> Discover(string baseDirectory, ILogger logger)
    {
        var root = Path.Combine(baseDirectory, PluginFolderName);
        if (!Directory.Exists(root))
        {
            logger.LogWarning("No plugins folder at {Root}. Layers 3, 4 and 5 are inactive.", root);
            return Array.Empty<LoadedPlugin>();
        }

        var found = new List<LoadedPlugin>();

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(folder);
            var mainAssembly = Path.Combine(folder, name + ".dll");
            if (!File.Exists(mainAssembly))
            {
                logger.LogWarning("Plugin folder {Folder} has no {Name}.dll. Skipped.", folder, name);
                continue;
            }

            try
            {
                var context = new PluginLoadContext(mainAssembly);
                var assembly = context.LoadFromAssemblyPath(mainAssembly);

                foreach (var type in SafeGetTypes(assembly, logger))
                {
                    if (!typeof(IPluginModule).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface) continue;
                    if (Activator.CreateInstance(type) is not IPluginModule module) continue;

                    found.Add(new LoadedPlugin(module, assembly));
                    logger.LogInformation("Discovered layer {Layer} module {Name} from {Assembly}.",
                        (int)module.Layer, module.DisplayName, assembly.GetName().Name);
                }
            }
            catch (Exception ex)
            {
                // A broken plugin must not stop the product from starting.
                logger.LogError(ex, "Failed to load plugin {Name}. It will be inactive.", name);
            }
        }

        return found.OrderBy(p => p.Module.SeqNo).ToList();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly, ILogger logger)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger.LogWarning(ex, "Partial type load in {Assembly}.", assembly.GetName().Name);
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }
}

public sealed record LoadedPlugin(IPluginModule Module, Assembly Assembly);
