using System.Text.RegularExpressions;
using Xunit;

namespace HrSuite.ArchitectureTests;

/// <summary>
/// Metadata alone is not enough: the C# compiler drops an unused assembly reference, so a
/// stray ProjectReference to layer 3/4/5 would slip past a reflection-only check until the
/// day someone writes the first `using`. These tests read the project files themselves.
/// </summary>
public class ProjectFileTests
{
    private static readonly string[] BaseCodeProjects =
    {
        "HrSuite.Common", "HrSuite.Core", "HrSuite.Infrastructure", "HrSuite.API"
    };

    private static readonly Regex ProjectReferenceElement =
        new(@"<ProjectReference\b[^>]*Include\s*=\s*""(?<inc>[^""]+)""[^>]*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void Base_code_project_files_never_reference_an_upper_layer_project()
    {
        var offenders = new List<string>();

        foreach (var project in BaseCodeProjects)
        {
            var path = Path.Combine(LayerNames.RepoRoot, project, project + ".csproj");
            Assert.True(File.Exists(path), $"Missing project file: {path}");

            foreach (Match m in ProjectReferenceElement.Matches(File.ReadAllText(path)))
            {
                var include = m.Groups["inc"].Value;

                // The plugin drop-in item group is allowed: it only sequences the build and
                // is declared with ReferenceOutputAssembly="false", so no metadata is emitted.
                if (include.StartsWith("@(", StringComparison.Ordinal)) continue;

                if (LayerNames.UpperLayerNamespaces.Any(ns => include.Contains(ns, StringComparison.OrdinalIgnoreCase)))
                    offenders.Add($"{project}.csproj -> {include}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A base-code project references an upper layer at compile time. Layers 3, 4 and 5 are " +
            "discovered at runtime from the plugins folder:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", offenders));
    }

    [Fact]
    public void Plugin_project_references_are_declared_without_output_assembly()
    {
        var path = Path.Combine(LayerNames.RepoRoot, "HrSuite.API", "HrSuite.API.csproj");
        var text = File.ReadAllText(path);

        Assert.Contains("PluginProject", text);

        var pluginRef = ProjectReferenceElement.Matches(text)
            .FirstOrDefault(m => m.Groups["inc"].Value.StartsWith("@(PluginProject", StringComparison.Ordinal));

        Assert.True(pluginRef is not null, "HrSuite.API must sequence plugin builds via @(PluginProject).");
        Assert.Contains("ReferenceOutputAssembly=\"false\"", pluginRef!.Value);
    }

    [Fact]
    public void Every_upper_layer_project_is_listed_as_a_plugin()
    {
        var text = File.ReadAllText(Path.Combine(LayerNames.RepoRoot, "HrSuite.API", "HrSuite.API.csproj"));

        var expected = Directory.EnumerateDirectories(LayerNames.RepoRoot)
            .Select(Path.GetFileName)
            .Where(n => n is not null && LayerNames.UpperLayerNamespaces.Any(ns => n.StartsWith(ns, StringComparison.Ordinal)))
            .ToArray();

        var missing = expected
            .Where(n => !text.Contains(Path.Combine(n!, n + ".csproj"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(missing.Length == 0,
            "Upper-layer projects not wired into the plugin drop: " + string.Join(", ", missing!));
    }
}
