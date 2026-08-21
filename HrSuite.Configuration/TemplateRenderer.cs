using System.Text.RegularExpressions;
using HrSuite.Core.Abstractions;

namespace HrSuite.Configuration;

/// <summary>
/// Layer 2 text templates. The body lives in cfg_setting under a key such as
/// <c>template.leave.approved</c>, so wording changes are a database edit, not a release.
///
/// The syntax is deliberately tiny — {{token}} substitution and nothing else. Anything that
/// needs a condition or a loop is behaviour, and behaviour belongs in Layer 5.
/// </summary>
public sealed class TemplateRenderer : ITemplateRenderer
{
    private static readonly Regex Token = new(@"\{\{\s*(?<key>[A-Za-z0-9_.]+)\s*\}\}", RegexOptions.Compiled);

    private readonly IConfigResolver _config;

    public TemplateRenderer(IConfigResolver config) => _config = config;

    public async Task<string> RenderAsync(
        string templateKey,
        IReadOnlyDictionary<string, object?> tokens,
        string fallback = "",
        CancellationToken ct = default)
    {
        var template = await _config.GetStringAsync(templateKey, ct).ConfigureAwait(false);
        return Render(string.IsNullOrWhiteSpace(template) ? fallback : template, tokens);
    }

    public string Render(string template, IReadOnlyDictionary<string, object?> tokens)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        return Token.Replace(template, match =>
        {
            var key = match.Groups["key"].Value;
            // An unknown token renders as empty rather than leaking the placeholder to a user.
            return tokens.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        });
    }
}
