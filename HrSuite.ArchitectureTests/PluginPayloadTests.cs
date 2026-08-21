using Xunit;

namespace HrSuite.ArchitectureTests;

/// <summary>
/// A plugin is deployed as a folder and loaded through <c>PluginLoadContext</c>, which resolves
/// shared contracts from the default context and everything else from beside the plugin. So the
/// folder has to be complete.
///
/// It was not. Layer 5 references Jint and Layer 4 references MailKit, but a class library does
/// not copy package assemblies to its own output by default, so
/// <c>plugins\HrSuite.Extensions.Engine\</c> held the engine and its project references and
/// nothing else. Discovery succeeded, registration succeeded, the controllers routed — and the
/// first request that actually ran a script died with
/// <c>Could not load file or assembly 'Jint'</c>.
///
/// Every other guard missed it. The unit tests reference the engine project directly, so Jint
/// resolves through the ordinary probing path there and 44 sandbox tests pass against an
/// assembly the deployed host cannot load. That gap is what these tests close: they check the
/// build output, which is the only place the difference shows.
/// </summary>
public class PluginPayloadTests
{
    private static string PluginsRoot =>
        Path.Combine(LayerNames.RepoRoot, "HrSuite.API", "bin", Configuration, "net8.0", "plugins");

    private static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static string[] PluginProjects() => Directory
        .EnumerateDirectories(LayerNames.RepoRoot)
        .Select(Path.GetFileName)
        .Where(n => n is not null && LayerNames.UpperLayerNamespaces.Any(ns => n.StartsWith(ns, StringComparison.Ordinal)))
        .Select(n => n!)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void Every_plugin_project_copies_its_private_package_dependencies()
    {
        var offenders = PluginProjects()
            .Where(name =>
            {
                var csproj = Path.Combine(LayerNames.RepoRoot, name, name + ".csproj");
                return File.Exists(csproj)
                       && !File.ReadAllText(csproj)
                           .Contains("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>",
                               StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These plugins do not copy their NuGet dependencies into their own output, so the " +
            "plugins folder will be incomplete and the failure will not appear until a request " +
            "reaches the code that uses the package: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_package_reference_a_plugin_declares_is_present_beside_it()
    {
        if (!Directory.Exists(PluginsRoot)) return;   // host not built yet; nothing to check

        var missing = new List<string>();

        foreach (var name in PluginProjects())
        {
            var csproj = Path.Combine(LayerNames.RepoRoot, name, name + ".csproj");
            if (!File.Exists(csproj)) continue;

            var folder = Path.Combine(PluginsRoot, name);
            if (!Directory.Exists(folder))
            {
                missing.Add($"{name}: no folder under plugins\\");
                continue;
            }

            var shipped = Directory.EnumerateFiles(folder, "*.dll")
                .Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var package in PackageReferences(File.ReadAllText(csproj)))
            {
                // The package id is the assembly name for every package used here. A package
                // whose assembly is named differently would need naming explicitly — better a
                // false alarm that gets read than a silent gap that does not.
                if (!shipped.Contains(package))
                    missing.Add($"{name}: {package}.dll");
            }
        }

        Assert.True(missing.Count == 0,
            "A plugin folder is missing a dependency the plugin declares. PluginLoadContext " +
            "resolves private dependencies from beside the plugin, so this throws " +
            "FileNotFoundException at run time, not build time:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", missing));
    }

    [Fact]
    public void Every_plugin_ships_the_deps_file_its_load_context_resolves_through()
    {
        if (!Directory.Exists(PluginsRoot)) return;

        var missing = PluginProjects()
            .Where(name => !File.Exists(Path.Combine(PluginsRoot, name, name + ".deps.json")))
            .ToArray();

        Assert.True(missing.Length == 0,
            "AssemblyDependencyResolver reads <plugin>.deps.json to locate private dependencies. " +
            "Without it the folder is just files: " + string.Join(", ", missing));
    }

    private static IEnumerable<string> PackageReferences(string csprojText)
        => System.Text.RegularExpressions.Regex
            .Matches(csprojText, @"<PackageReference\b[^>]*Include\s*=\s*""(?<id>[^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => m.Groups["id"].Value);
}
