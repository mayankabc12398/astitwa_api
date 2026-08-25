using System.Text.RegularExpressions;

namespace HrSuite.Infrastructure.Schema;

/// <summary>
/// The only place in the product that turns a field definition into DDL text, and the only
/// place that decides whether a column may be touched at all.
///
/// Nothing here interpolates anything a client sent without first proving it matches
/// <see cref="CustomColumnPattern"/> (a column this feature created) or a plain identifier
/// resolved from the registry or INFORMATION_SCHEMA (a table, or a column to sit after). SQL
/// types come from <see cref="ControlTypes"/>: a type named in a request is ignored, always.
///
/// Pure and side-effect free, so what it will emit can be read here rather than inferred from
/// a database. <see cref="SchemaExecutor"/> owns the connection, the audit row and the risk.
/// </summary>
public static class ColumnDdl
{
    /// <summary>
    /// Every column this feature creates carries this prefix.
    ///
    /// It is not decoration — it is the whole safety model. A request naming `full_name` gets
    /// nowhere, because the guard below refuses any identifier that does not start with it,
    /// whatever the registry, the metadata row or the caller claims.
    /// </summary>
    public const string CustomPrefix = "cf_";

    /// <summary>cf_ plus 1..58 lowercase letters, digits or underscores — MySQL's limit is 64.</summary>
    private static readonly Regex CustomColumnPattern = new(@"^cf_[a-z0-9_]{1,58}$", RegexOptions.Compiled);

    /// <summary>The shape a table or anchor name must still have after being read from the registry.</summary>
    private static readonly Regex PlainIdentifierPattern = new(@"^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    /// <summary>
    /// Control type to the column it produces. This map is the entire vocabulary: a control
    /// type that is not listed cannot produce DDL, which is what stops a new control type from
    /// silently inventing a column type nobody reviewed.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ControlTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = "VARCHAR(255)",
            ["textarea"] = "TEXT",
            ["number"] = "INT",
            ["decimal"] = "DECIMAL(18,4)",
            ["date"] = "DATE",
            ["datetime"] = "DATETIME",
            ["checkbox"] = "TINYINT(1)",
            ["dropdown"] = "VARCHAR(255)",
            ["radio"] = "VARCHAR(255)",
            ["multiselect"] = "TEXT",
            ["file"] = "VARCHAR(500)",
        };

    /// <summary>Control types that carry an option list.</summary>
    public static readonly IReadOnlySet<string> ChoiceControlTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dropdown", "radio", "multiselect" };

    public static readonly IReadOnlySet<string> NumericControlTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "number", "decimal" };

    public static readonly IReadOnlySet<string> DateControlTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "date", "datetime" };

    /// <summary>
    /// Type changes that widen rather than truncate, and are therefore safe to apply in place.
    ///
    /// Anything outside this map is refused with "create a new field instead". A change that
    /// might truncate is not a change worth making automatically: the data it would cut off is
    /// somebody's record, and no undo exists once the ALTER has run.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> SafeWidening =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = new[] { "textarea" },
            ["dropdown"] = new[] { "text", "textarea", "radio", "multiselect" },
            ["radio"] = new[] { "text", "textarea", "dropdown", "multiselect" },
            ["number"] = new[] { "decimal", "text", "textarea" },
            ["date"] = new[] { "datetime" },
            ["checkbox"] = new[] { "number", "text" },
            ["multiselect"] = new[] { "textarea" },
        };

    /// <summary>
    /// MySQL reserved words. Refused even with the cf_ prefix in front of them, because a
    /// reserved name forces every consumer — every query, every export, every report — to
    /// quote it correctly forever, and one that forgets fails at run time.
    /// </summary>
    private static readonly IReadOnlySet<string> ReservedWords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "add", "all", "alter", "analyze", "and", "as", "asc", "before", "between", "bigint", "binary",
            "blob", "both", "by", "call", "cascade", "case", "change", "char", "character", "check", "collate",
            "column", "condition", "constraint", "continue", "convert", "create", "cross", "current_date",
            "current_time", "current_timestamp", "current_user", "cursor", "database", "databases", "dec",
            "decimal", "declare", "default", "delayed", "delete", "desc", "describe", "distinct", "div",
            "double", "drop", "dual", "each", "else", "elseif", "enclosed", "escaped", "exists", "exit",
            "explain", "false", "fetch", "float", "for", "force", "foreign", "from", "fulltext", "grant",
            "group", "having", "if", "ignore", "in", "index", "infile", "inner", "inout", "insensitive",
            "insert", "int", "integer", "interval", "into", "is", "iterate", "join", "key", "keys", "kill",
            "leading", "leave", "left", "like", "limit", "lines", "load", "lock", "long", "longblob",
            "longtext", "loop", "match", "maxvalue", "mediumblob", "mediumint", "mediumtext", "mod",
            "modifies", "natural", "not", "null", "numeric", "on", "optimize", "option", "optionally", "or",
            "order", "out", "outer", "outfile", "precision", "primary", "procedure", "purge", "range", "read",
            "reads", "real", "references", "regexp", "release", "rename", "repeat", "replace", "require",
            "restrict", "return", "revoke", "right", "rlike", "schema", "schemas", "select", "sensitive",
            "separator", "set", "show", "smallint", "spatial", "specific", "sql", "ssl", "starting", "table",
            "terminated", "then", "tinyblob", "tinyint", "tinytext", "to", "trailing", "trigger", "true",
            "undo", "union", "unique", "unlock", "unsigned", "update", "usage", "use", "using", "values",
            "varbinary", "varchar", "varying", "when", "where", "while", "with", "write", "xor", "zerofill",
        };

    /// <summary>Turns a label into a candidate column name: "Cost Centre" -> cf_cost_centre.</summary>
    public static string SlugColumnName(string? label)
    {
        var slug = Regex.Replace((label ?? string.Empty).ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        if (slug.Length == 0) slug = "field";
        if (slug.Length > 58) slug = slug[..58].TrimEnd('_');
        return CustomPrefix + slug;
    }

    /// <summary>The reason a column name is unacceptable, or null when it is fine.</summary>
    public static string? ValidateCustomColumnName(string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
            return "A column name is required.";
        if (!column.StartsWith(CustomPrefix, StringComparison.Ordinal))
            return $"A column added here has to start with '{CustomPrefix}'.";
        if (!CustomColumnPattern.IsMatch(column))
            return $"'{column}' is not a usable column name — {CustomPrefix} followed by lowercase letters, digits or underscores, up to 61 characters.";
        if (ReservedWords.Contains(column))
            return $"'{column}' is a MySQL reserved word.";
        return null;
    }

    public static bool IsPlainIdentifier(string? identifier) =>
        !string.IsNullOrWhiteSpace(identifier) && PlainIdentifierPattern.IsMatch(identifier);

    /// <summary>True for a column this feature created, and may therefore alter or drop.</summary>
    public static bool IsCustomColumn(string? column) =>
        !string.IsNullOrWhiteSpace(column) && CustomColumnPattern.IsMatch(column);

    /// <summary>The column type a control produces. MaxLength narrows plain text and nothing else.</summary>
    public static string? ResolveSqlType(string? controlType, int? maxLength)
    {
        if (string.IsNullOrWhiteSpace(controlType) || !ControlTypes.TryGetValue(controlType, out var sqlType))
            return null;

        if (controlType.Equals("text", StringComparison.OrdinalIgnoreCase) && maxLength is > 0 and <= 4000)
            return $"VARCHAR({maxLength})";

        return sqlType;
    }

    public static bool IsSafeControlTypeChange(string from, string to) =>
        string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
        || (SafeWidening.TryGetValue(from, out var targets) && targets.Contains(to, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// ADD COLUMN, always nullable.
    ///
    /// Nullable even for a required field: every row already in the table predates the column,
    /// and so does every INSERT written before today. "Required" is enforced where it can be
    /// explained to somebody — on the form and in the service — rather than by a constraint
    /// that would reject the rest of the application's writes.
    /// </summary>
    public static string BuildAddColumn(string table, string column, string sqlType, string? afterColumn)
    {
        Guard(table, column, afterColumn);
        var after = string.IsNullOrWhiteSpace(afterColumn) ? string.Empty : $" AFTER `{afterColumn}`";
        return $"ALTER TABLE `{table}` ADD COLUMN `{column}` {sqlType} NULL{after};";
    }

    /// <summary>CHANGE COLUMN — a rename, a proven-safe widening, or both at once.</summary>
    public static string BuildChangeColumn(string table, string fromColumn, string toColumn, string sqlType)
    {
        Guard(table, fromColumn, null);
        Guard(table, toColumn, null);
        return $"ALTER TABLE `{table}` CHANGE COLUMN `{fromColumn}` `{toColumn}` {sqlType} NULL;";
    }

    /// <summary>MODIFY COLUMN, used to move an existing column to a new position.</summary>
    public static string BuildMoveColumn(string table, string column, string sqlType, string? afterColumn)
    {
        Guard(table, column, afterColumn);
        var after = string.IsNullOrWhiteSpace(afterColumn) ? " FIRST" : $" AFTER `{afterColumn}`";
        return $"ALTER TABLE `{table}` MODIFY COLUMN `{column}` {sqlType} NULL{after};";
    }

    /// <summary>DROP COLUMN. The caller archives the values first; this cannot put them back.</summary>
    public static string BuildDropColumn(string table, string column)
    {
        Guard(table, column, null);
        return $"ALTER TABLE `{table}` DROP COLUMN `{column}`;";
    }

    /// <summary>Reads one column of a table, keyed by its primary key — the archive's source.</summary>
    public static string BuildReadColumn(string table, string pkColumn, string column)
    {
        Guard(table, column, null);
        if (!IsPlainIdentifier(pkColumn))
            throw new InvalidOperationException($"Unsafe key identifier '{pkColumn}'.");
        return $"SELECT `{pkColumn}` AS record_id, `{column}` AS value_text FROM `{table}` WHERE `{column}` IS NOT NULL;";
    }

    /// <summary>
    /// Reads the builder-added columns of one record.
    ///
    /// The record's own id travels as a parameter; only the column list is built from
    /// identifiers, and every one of them has to be a cf_ column of this table.
    /// </summary>
    public static string BuildSelectExtra(string table, string pkColumn, IReadOnlyList<string> columns)
    {
        GuardTableAndKey(table, pkColumn);
        foreach (var column in columns) GuardColumn(column);
        var list = string.Join(", ", columns.Select(c => $"`{c}`"));
        return $"SELECT {list} FROM `{table}` WHERE `{pkColumn}` = @RecordId;";
    }

    /// <summary>
    /// Writes the builder-added columns of one record.
    ///
    /// Values are parameters named after their position, never inlined — the caller supplies
    /// them as @v0, @v1 … in the same order as <paramref name="columns"/>.
    /// </summary>
    public static string BuildUpdateExtra(string table, string pkColumn, IReadOnlyList<string> columns)
    {
        GuardTableAndKey(table, pkColumn);
        foreach (var column in columns) GuardColumn(column);
        var sets = string.Join(", ", columns.Select((c, i) => $"`{c}` = @v{i}"));
        return $"UPDATE `{table}` SET {sets} WHERE `{pkColumn}` = @RecordId;";
    }

    private static void GuardTableAndKey(string table, string pkColumn)
    {
        if (!IsPlainIdentifier(table))
            throw new InvalidOperationException($"Unsafe table identifier '{table}'.");
        if (!IsPlainIdentifier(pkColumn))
            throw new InvalidOperationException($"Unsafe key identifier '{pkColumn}'.");
    }

    private static void GuardColumn(string column)
    {
        if (!IsCustomColumn(column))
            throw new InvalidOperationException(
                $"Refusing to read or write '{column}' — only {CustomPrefix} columns created by the field builder are reachable this way.");
    }

    /// <summary>
    /// The last check before an identifier reaches a SQL string.
    ///
    /// Throws rather than returning false: arriving here with a bad identifier means a caller
    /// skipped a validation it was supposed to run, and a quiet fallback would turn that
    /// mistake into a schema change nobody asked for.
    /// </summary>
    private static void Guard(string table, string column, string? afterColumn)
    {
        if (!IsPlainIdentifier(table))
            throw new InvalidOperationException($"Unsafe table identifier '{table}'.");
        if (!IsCustomColumn(column))
            throw new InvalidOperationException(
                $"Refusing to build DDL for '{column}' — only {CustomPrefix} columns created by the field builder can be altered.");
        if (!string.IsNullOrWhiteSpace(afterColumn) && !IsPlainIdentifier(afterColumn))
            throw new InvalidOperationException($"Unsafe anchor identifier '{afterColumn}'.");
    }
}
