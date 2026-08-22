using System.Globalization;

namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// Turns whatever a caller sent into the type the endpoint declared.
///
/// A value that will not convert becomes null rather than an error: the declaration says
/// what the query expects, and a query written against a nullable parameter already has to
/// say what it does with one. Guessing — treating "abc" as 0 — would answer with rows the
/// caller never asked for.
/// </summary>
internal static class ParamCoercion
{
    public static object? To(object? raw, string? type)
    {
        if (raw is null) return null;

        var text = raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture);
        if (text is null) return null;

        return (type ?? "string").ToLowerInvariant() switch
        {
            "int" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
            "decimal" => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null,
            "bool" => ParseBool(text),
            "date" => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null,
            _ => text
        };
    }

    private static object? ParseBool(string text)
    {
        if (bool.TryParse(text, out var parsed)) return parsed;
        if (text == "1") return true;
        if (text == "0") return false;
        return null;
    }
}
