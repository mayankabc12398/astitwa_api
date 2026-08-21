namespace HrSuite.Core.Abstractions;

/// <summary>
/// Layer 2 contract. Base code asks for a template by key and gets text back; it never
/// holds the wording itself.
/// </summary>
public interface ITemplateRenderer
{
    Task<string> RenderAsync(
        string templateKey,
        IReadOnlyDictionary<string, object?> tokens,
        string fallback = "",
        CancellationToken ct = default);

    string Render(string template, IReadOnlyDictionary<string, object?> tokens);
}
