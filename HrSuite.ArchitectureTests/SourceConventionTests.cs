using System.Text.RegularExpressions;
using Xunit;

namespace HrSuite.ArchitectureTests;

/// <summary>
/// Rules that live in the source text rather than in the type graph:
/// no inline SQL anywhere, and no client-specific literal in base code.
/// </summary>
public class SourceConventionTests
{
    private static readonly string[] ProjectFolders =
    {
        "HrSuite.Common", "HrSuite.Core", "HrSuite.Infrastructure", "HrSuite.API",
        "HrSuite.Configuration", "HrSuite.Addons.Payroll",
        "HrSuite.Integrations.Email", "HrSuite.Extensions.Engine"
    };

    /// <summary>
    /// The API Builder, and nothing else.
    ///
    /// "Stored procedures only" exists so that no ordinary data path can be talked into
    /// running text. These two files ARE the exception the product decided to make: the
    /// statement is written by an administrator and stored as a row, which is the whole
    /// feature. Naming them here keeps the rule enforced everywhere else — a third file
    /// that starts building SQL still fails, which is the point of listing them rather
    /// than exempting the folder.
    ///
    /// What stands in for the rule in these two is in SqlGuard: one read-only statement,
    /// bound parameters only, a mandatory tenant token, and a READ ONLY transaction
    /// around the run.
    /// </summary>
    private static readonly string[] SqlTextExemptions =
    {
        Path.Combine("HrSuite.Extensions.Engine", "Runtime", "SqlGuard.cs"),
        Path.Combine("HrSuite.Extensions.Engine", "Runtime", "CustomApiRunner.cs"),
        // The Screen Field Builder. A schema change cannot be a stored procedure without
        // giving one a statement-to-execute parameter, which would move the guard into SQL
        // where it cannot be tested. What stands in for the rule: ColumnDdl builds every
        // statement from identifiers matched against a regex and resolved from the registry,
        // only cf_ columns can be altered at all, and every statement is audited.
        Path.Combine("HrSuite.Infrastructure", "Schema", "ColumnDdl.cs"),
        Path.Combine("HrSuite.Infrastructure", "Schema", "SchemaExecutor.cs")
    };

    private static bool IsExempt(string path)
        => SqlTextExemptions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string Path, string Text)> SourceFiles(params string[] folders)
    {
        foreach (var folder in folders)
        {
            var root = Path.Combine(LayerNames.RepoRoot, folder);
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                yield return (Path.GetRelativePath(LayerNames.RepoRoot, file), File.ReadAllText(file));
            }
        }
    }

    [Fact]
    public void No_project_contains_inline_sql()
    {
        // Stored procedures only. Dapper must never receive a SQL string.
        var sql = new Regex(
            @"(?ix)  (""|@"") [^""]*? \b (select \s+ .*? \s+ from | insert \s+ into | update \s+ \w+ \s+ set | delete \s+ from | create \s+ table | drop \s+ table) \b",
            RegexOptions.Compiled);

        var offenders = SourceFiles(ProjectFolders)
            .Where(f => sql.IsMatch(f.Text) && !IsExempt(f.Path))
            .Select(f => f.Path)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Inline SQL found. Every data operation must go through a stored procedure:" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
    }

    [Fact]
    public void No_project_executes_a_command_as_raw_text()
    {
        var offenders = SourceFiles(ProjectFolders)
            .Where(f => f.Text.Contains("CommandType.Text", StringComparison.Ordinal) && !IsExempt(f.Path))
            .Select(f => f.Path)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "CommandType.Text found. Dapper calls must use CommandType.StoredProcedure:" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
    }

    [Fact]
    public void Base_code_contains_no_hardcoded_tenant_branch()
    {
        // "if (tenantId == 14)" and friends. Tenant-specific behaviour belongs in layer 2 or 5.
        var branch = new Regex(
            @"(?ix) \b tenant(id)? \b \s* (==|!=) \s* \d+ | \b (==|!=) \s* \b tenant(id)? \b \s* \d+",
            RegexOptions.Compiled);

        var offenders = SourceFiles("HrSuite.Common", "HrSuite.Core", "HrSuite.Infrastructure", "HrSuite.API")
            .Where(f => branch.IsMatch(f.Text))
            .Select(f => f.Path)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "A tenant id is branched on in base code. Push it to cfg_setting or ext_script_hook:" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
    }

    [Fact]
    public void Base_code_never_names_an_upper_layer_namespace_in_a_using()
    {
        var offenders = new List<string>();

        foreach (var (path, text) in SourceFiles("HrSuite.Common", "HrSuite.Core", "HrSuite.Infrastructure", "HrSuite.API"))
        {
            foreach (var ns in LayerNames.UpperLayerNamespaces)
            {
                if (Regex.IsMatch(text, $@"^\s*(global\s+)?using\s+(static\s+)?{Regex.Escape(ns)}\b", RegexOptions.Multiline))
                    offenders.Add($"{path} -> using {ns}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Layer 1 imports an upper layer:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
    }

    [Fact]
    public void Every_stored_procedure_called_from_code_exists_in_the_sql_scripts()
    {
        var dbFolder = Path.Combine(LayerNames.RepoRoot, "db");
        if (!Directory.Exists(dbFolder)) return; // scripts land in phase 1

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(dbFolder, "*.sql", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file),
                         @"CREATE\s+PROCEDURE\s+`?(?<n>sp_[A-Za-z0-9_]+)`?", RegexOptions.IgnoreCase))
                declared.Add(m.Groups["n"].Value);
        }

        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, text) in SourceFiles(ProjectFolders))
        {
            foreach (Match m in Regex.Matches(text, @"""(?<n>sp_[A-Za-z0-9_]+)"""))
                if (!declared.Contains(m.Groups["n"].Value)) missing.Add(m.Groups["n"].Value);
        }

        Assert.True(missing.Count == 0,
            "Code calls stored procedures that no script under db/ creates: " + string.Join(", ", missing));
    }
}
