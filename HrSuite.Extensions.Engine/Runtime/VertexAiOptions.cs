namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// Where the Vertex AI credential lives and which model the editor talks to.
///
/// The credential itself is never in this repository and never in appsettings.json.
/// CredentialsPath points at a service-account JSON file outside the source tree, so
/// rotating the key — which is a thing that has to be easy, because it has to be done
/// the moment anyone suspects the old one leaked — is replacing one file.
/// </summary>
public sealed class VertexAiOptions
{
    public const string SectionName = "VertexAi";

    /// <summary>Absolute path to the service-account JSON. Preferred over the inline form.</summary>
    public string? CredentialsPath { get; set; }

    /// <summary>The same JSON inline, for hosts where a file is awkward (containers, App Service).</summary>
    public string? ServiceAccountJson { get; set; }

    /// <summary>Defaults to the project_id inside the credential.</summary>
    public string? ProjectId { get; set; }

    public string Location { get; set; } = "us-central1";

    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>Seconds. A model call is slower than a database call and needs its own budget.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(CredentialsPath) || !string.IsNullOrWhiteSpace(ServiceAccountJson);
}
