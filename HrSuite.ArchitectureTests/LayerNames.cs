using System.Reflection;

namespace HrSuite.ArchitectureTests;

/// <summary>Single place that names the layers, so a new project cannot silently escape the rules.</summary>
public static class LayerNames
{
    public const string Root = "HrSuite";

    /// <summary>Layer 1 — base code. May never depend on layers 3, 4 or 5.</summary>
    public static readonly string[] BaseCodeAssemblies =
    {
        "HrSuite.Common",
        "HrSuite.Core",
        "HrSuite.Infrastructure",
        "HrSuite.API"
    };

    /// <summary>Namespace prefixes that Layer 1 must never reference.</summary>
    public static readonly string[] UpperLayerNamespaces =
    {
        "HrSuite.Addons",
        "HrSuite.Integrations",
        "HrSuite.Extensions"
    };

    public static Assembly Load(string name) => Assembly.Load(name);

    public static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HrSuite.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("HrSuite.slnx not found above the test output folder.");
    }
}
