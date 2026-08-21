using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace HrSuite.ArchitectureTests;

/// <summary>
/// The dependency rule from section 3.1: a lower layer may reference an upper layer, never the reverse.
/// These tests are the mechanical enforcement demanded by section 3.2 — discipline alone is not enough.
/// </summary>
public class DependencyRuleTests
{
    [Fact]
    public void Core_has_no_reference_to_addons_integrations_or_extensions()
    {
        var result = Types.InAssembly(LayerNames.Load("HrSuite.Core"))
            .ShouldNot()
            .HaveDependencyOnAny(LayerNames.UpperLayerNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe("HrSuite.Core", result));
    }

    [Theory]
    [InlineData("HrSuite.Common")]
    [InlineData("HrSuite.Core")]
    [InlineData("HrSuite.Infrastructure")]
    [InlineData("HrSuite.API")]
    public void Base_code_has_no_reference_to_upper_layers(string assemblyName)
    {
        var result = Types.InAssembly(LayerNames.Load(assemblyName))
            .ShouldNot()
            .HaveDependencyOnAny(LayerNames.UpperLayerNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(assemblyName, result));
    }

    [Fact]
    public void Base_code_assemblies_do_not_even_link_upper_layer_assemblies()
    {
        foreach (var name in LayerNames.BaseCodeAssemblies)
        {
            var referenced = LayerNames.Load(name)
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => LayerNames.UpperLayerNamespaces.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
                .ToArray();

            Assert.True(referenced.Length == 0,
                $"{name} links upper-layer assemblies: {string.Join(", ", referenced)}. " +
                "Layer 3/4/5 assemblies are discovered at runtime, never referenced at compile time.");
        }
    }

    [Fact]
    public void Common_depends_on_nothing_in_the_product()
    {
        var result = Types.InAssembly(LayerNames.Load("HrSuite.Common"))
            .ShouldNot()
            .HaveDependencyOnAny("HrSuite.Core", "HrSuite.Infrastructure", "HrSuite.API", "HrSuite.Configuration")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe("HrSuite.Common", result));
    }

    [Fact]
    public void Core_does_not_depend_on_infrastructure_or_the_api_host()
    {
        var result = Types.InAssembly(LayerNames.Load("HrSuite.Core"))
            .ShouldNot()
            .HaveDependencyOnAny("HrSuite.Infrastructure", "HrSuite.API", "HrSuite.Configuration")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe("HrSuite.Core", result));
    }

    [Fact]
    public void Core_does_not_depend_on_a_database_driver()
    {
        var result = Types.InAssembly(LayerNames.Load("HrSuite.Core"))
            .ShouldNot()
            .HaveDependencyOnAny("Dapper", "MySqlConnector", "System.Data.Common")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe("HrSuite.Core", result));
    }

    [Theory]
    [InlineData("HrSuite.Addons.Payroll")]
    [InlineData("HrSuite.Integrations.Email")]
    [InlineData("HrSuite.Extensions.Engine")]
    public void Upper_layer_assembly_exposes_exactly_one_plugin_module(string assemblyName)
    {
        var modules = LayerNames.Load(assemblyName)
            .GetTypes()
            .Where(t => typeof(Core.Modularity.IPluginModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .ToArray();

        Assert.True(modules.Length == 1,
            $"{assemblyName} must expose exactly one IPluginModule so the host can discover it by scan. Found {modules.Length}.");
    }

    [Theory]
    [InlineData("HrSuite.Addons.Payroll")]
    [InlineData("HrSuite.Integrations.Email")]
    [InlineData("HrSuite.Extensions.Engine")]
    public void Upper_layer_assembly_does_not_reach_sideways_into_another_upper_layer(string assemblyName)
    {
        var others = LayerNames.UpperLayerNamespaces
            .Where(ns => !assemblyName.StartsWith(ns, StringComparison.Ordinal))
            .ToArray();

        var result = Types.InAssembly(LayerNames.Load(assemblyName))
            .ShouldNot()
            .HaveDependencyOnAny(others)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(assemblyName, result));
    }

    private static string Describe(string assemblyName, TestResult result)
    {
        var offenders = result.FailingTypeNames ?? Enumerable.Empty<string>();
        return $"Dependency rule violated in {assemblyName}:{Environment.NewLine}  " +
               string.Join(Environment.NewLine + "  ", offenders);
    }
}
