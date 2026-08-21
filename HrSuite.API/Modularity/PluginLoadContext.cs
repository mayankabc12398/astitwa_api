using System.Reflection;
using System.Runtime.Loader;

namespace HrSuite.API.Modularity;

/// <summary>
/// Loads one Layer 3/4/5 assembly and its private dependencies from its own folder.
///
/// Anything the host already knows about — the shared framework, HrSuite.Core,
/// Microsoft.Extensions.* — is deliberately resolved from the default context so the
/// plugin and the host see the same IServiceCollection type identity. Only dependencies
/// the host has never heard of (Jint, MailKit) come from the plugin folder.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginMainAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginMainAssemblyPath), isCollectible: false)
        => _resolver = new AssemblyDependencyResolver(pluginMainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Shared identity first. Never load a second copy of a contract assembly.
        try
        {
            var shared = Default.LoadFromAssemblyName(assemblyName);
            if (shared is not null) return shared;
        }
        catch (FileNotFoundException) { /* host does not have it - fall through */ }
        catch (FileLoadException) { /* fall through */ }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
