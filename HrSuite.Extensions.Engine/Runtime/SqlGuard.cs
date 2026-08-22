using System.Text.RegularExpressions;
using HrSuite.Extensions.Engine.Models;

namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// What an administrator is allowed to write in the API Builder.
///
/// The permission behind that screen says the holder may READ this tenant's data. It does
/// not say they may write it, reach another tenant's rows, or reach the server's file
/// system — and a free-text SQL box says all three unless something stops it. This is that
/// something, and it runs twice: when an endpoint is saved, and again before every run, so
/// a row edited straight in the database cannot slip past the screen.
///
/// It is deliberately blunt. A construct that is merely hard to prove safe is refused, and
/// the author gets a message saying which rule they hit. Refusing a legitimate query
/// occasionally is a nuisance; letting one dangerous one through is not.
/// </summary>
public static class SqlGuard
{
    /// <summary>
    /// The token an endpoint must carry, replaced by a bound parameter at run time.
    ///
    /// It exists because the tenant filter cannot be optional and cannot be added
    /// automatically: only the author knows which table in their query holds tenant_id.
    /// Requiring the token means forgetting the filter is a save-time error rather than a
    /// leak discovered by whoever reads somebody else's payroll.
    /// </summary>
    public const string TenantToken = "{tenant}";

    /// <summary>The parameter the token becomes. Reserved — an author cannot declare it.</summary>
    public const string TenantParam = "__tenant_id";

    /// <summary>The parameter carrying the row cap for the wrapper around the author's SELECT.</summary>
    public const string MaxRowsParam = "__max_rows";

    private static readonly Regex Placeholder = new(@"@([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static readonly Regex ParamName = new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    public static readonly IReadOnlyList<string> ParamTypes = new[] { "string", "int", "decimal", "bool", "date" };

    /// <summary>
    /// Verbs that write, change shape, or change who may do either. Matched as whole words,
    /// so a column called "update_on" or a table called "hr_department" is left alone.
    /// </summary>
    private static readonly string[] ForbiddenWords =
    {
        "insert", "update", "delete", "replace", "merge", "truncate", "drop", "alter",
        "create", "rename", "grant", "revoke", "call", "do", "set", "lock", "unlock",
        "handler", "prepare", "execute", "deallocate", "load", "outfile", "dumpfile",
        "into", "sleep", "benchmark", "get_lock", "sys_exec", "load_file"
    };

    /// <summary>
    /// Schemas that describe the server rather than the tenant. Reading them tells a caller
    /// what else exists and how to reach it, which is the first half of every exploit.
    /// </summary>
    private static readonly string[] ForbiddenSchemas =
    {
        "information_schema", "performance_schema", "mysql.", "sys.", "ext_api_endpoint"
    };

    public sealed record Verdict(bool Ok, string? Error)
    {
        public static readonly Verdict Allowed = new(true, null);
        public static Verdict Refused(string reason) => new(false, reason);
    }

    /// <summary>
    /// Checks the SQL and the declared parameters together — a placeholder with no
    /// declaration and a declaration with no placeholder are both wrong, and neither is
    /// visible when the two are checked apart.
    /// </summary>
    public static Verdict Check(string? sqlText, IReadOnlyList<CustomApiParam>? declared)
    {
        var sql = (sqlText ?? string.Empty).Trim();

        if (sql.Length == 0) return Verdict.Refused("Write a SELECT statement.");
        if (sql.Length > 20000) return Verdict.Refused("The statement is too long to be one query.");

        // Comments first: everything below reads the text as written, and a comment is how a
        // second statement is smuggled past a reader that skips them.
        if (sql.Contains("--", StringComparison.Ordinal)
            || sql.Contains('#')
            || sql.Contains("/*", StringComparison.Ordinal))
        {
            return Verdict.Refused("Comments are not allowed in an endpoint. Remove --, # and /* */.");
        }

        if (sql.Contains(';'))
            return Verdict.Refused("One statement only. Remove the semicolon.");

        var lowered = sql.ToLowerInvariant();

        if (!lowered.StartsWith("select", StringComparison.Ordinal)
            && !lowered.StartsWith("with", StringComparison.Ordinal))
        {
            return Verdict.Refused("An endpoint reads. Start the statement with SELECT or WITH.");
        }

        foreach (var word in ForbiddenWords)
        {
            if (Regex.IsMatch(lowered, $@"\b{Regex.Escape(word)}\b"))
                return Verdict.Refused($"'{word.ToUpperInvariant()}' is not allowed in an endpoint.");
        }

        foreach (var schema in ForbiddenSchemas)
        {
            if (lowered.Contains(schema, StringComparison.Ordinal))
                return Verdict.Refused($"'{schema}' is out of reach of an endpoint.");
        }

        if (!sql.Contains(TenantToken, StringComparison.Ordinal))
        {
            return Verdict.Refused(
                $"Add the tenant filter. Write {TenantToken} where the tenant id belongs, " +
                "for example: WHERE e.tenant_id = " + TenantToken);
        }

        return CheckParameters(sql, declared ?? Array.Empty<CustomApiParam>());
    }

    private static Verdict CheckParameters(string sql, IReadOnlyList<CustomApiParam> declared)
    {
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var param in declared)
        {
            var name = (param.Name ?? string.Empty).Trim();

            if (!ParamName.IsMatch(name))
                return Verdict.Refused($"'{name}' is not a usable parameter name. Use letters, digits and underscores.");

            if (name.StartsWith("__", StringComparison.Ordinal))
                return Verdict.Refused($"'{name}' is reserved. Names starting with __ belong to the runner.");

            if (!ParamTypes.Contains((param.Type ?? string.Empty).ToLowerInvariant()))
                return Verdict.Refused($"'{name}' has an unknown type. Use one of: {string.Join(", ", ParamTypes)}.");

            if (!declaredNames.Add(name))
                return Verdict.Refused($"'{name}' is declared twice.");
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Placeholder.Matches(sql))
        {
            var name = match.Groups[1].Value;
            if (name.StartsWith("__", StringComparison.Ordinal))
                return Verdict.Refused($"'@{name}' is reserved. The runner supplies its own parameters.");

            used.Add(name);
        }

        var undeclared = used.FirstOrDefault(u => !declaredNames.Contains(u));
        if (undeclared is not null)
            return Verdict.Refused($"'@{undeclared}' is used but not declared. Add it to the parameters.");

        var unused = declaredNames.FirstOrDefault(d => !used.Contains(d));
        if (unused is not null)
            return Verdict.Refused($"'{unused}' is declared but never used. Write @{unused} in the statement or remove it.");

        return Verdict.Allowed;
    }

    /// <summary>
    /// The statement as it actually runs.
    ///
    /// The tenant token becomes a bound parameter, and the whole thing is wrapped so the row
    /// cap is applied by the database rather than by reading everything and throwing most of
    /// it away. A query that would return a million rows costs one page, not a million.
    /// </summary>
    public static string Compile(string sqlText)
        => $"SELECT * FROM ({sqlText.Trim().Replace(TenantToken, "@" + TenantParam, StringComparison.Ordinal)}) " +
           $"AS hrsuite_endpoint LIMIT @{MaxRowsParam}";

    /// <summary>A slug is a URL segment, so it is held to what a URL segment may be.</summary>
    public static bool IsValidSlug(string? slug)
        => !string.IsNullOrWhiteSpace(slug) && Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{1,79}$");
}
