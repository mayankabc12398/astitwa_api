using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// Talks to Vertex AI on behalf of the script editor.
///
/// This class exists so that the service-account key does not have to travel anywhere near
/// a browser. A key in the bundle is a key every user of the product holds, and it does not
/// grant "ask a model a question" — it grants whatever that service account can do in the
/// whole project. So the editor asks THIS API, this API asks Google, and the credential
/// never leaves the server.
///
/// The token is minted the way Google's own libraries mint it — a self-signed JWT exchanged
/// for an access token — rather than by taking a dependency on Google.Apis.Auth for forty
/// lines of work. It is cached until shortly before it expires, because a token request per
/// keystroke-driven question would double the latency of every answer.
/// </summary>
public sealed class VertexAiClient
{
    private const string Scope = "https://www.googleapis.com/auth/cloud-platform";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _http;
    private readonly VertexAiOptions _options;
    private readonly ILogger<VertexAiClient> _log;

    // One mint at a time. Without this every request that arrives while a token is being
    // fetched fetches its own, which is how a page load turns into six token requests.
    private readonly SemaphoreSlim _mintLock = new(1, 1);

    private ServiceAccount? _account;
    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public VertexAiClient(IHttpClientFactory http, IOptions<VertexAiOptions> options, ILogger<VertexAiClient> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string Model => _options.Model;

    /// <summary>
    /// One question, one answer. No conversation is kept here: the caller sends the turns it
    /// wants the model to see, which keeps this stateless and means two people using the
    /// editor cannot end up in each other's thread.
    /// </summary>
    public async Task<string> GenerateAsync(
        string systemInstruction,
        IReadOnlyList<(string Role, string Text)> turns,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Vertex AI is not configured on this server.");

        var account = LoadAccount();
        var projectId = string.IsNullOrWhiteSpace(_options.ProjectId) ? account.ProjectId : _options.ProjectId;
        var token = await AccessTokenAsync(account, ct).ConfigureAwait(false);

        var url =
            $"https://{_options.Location}-aiplatform.googleapis.com/v1/projects/{projectId}" +
            $"/locations/{_options.Location}/publishers/google/models/{_options.Model}:generateContent";

        var body = new
        {
            contents = turns.Select(t => new
            {
                role = t.Role == "model" ? "model" : "user",
                parts = new[] { new { text = t.Text } }
            }).ToArray(),
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            generationConfig = new
            {
                // Low, not zero: this writes code against a fixed contract, where invention
                // is the failure mode rather than the point.
                temperature = 0.2,
                maxOutputTokens = 4096,
                // The 2.5 models think before answering, which costs about four seconds of
                // silence. For an assistant answering questions about forty lines of code in
                // an editor, that trade is the wrong way round — the first words matter more
                // than the last few per cent of quality. Raise it for harder work.
                thinkingConfig = new { thinkingBudget = 0 }
            }
        };

        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _log.LogError("Vertex AI refused the request: {Status} {Body}", (int)response.StatusCode, text);
            throw new InvalidOperationException(FriendlyError(response.StatusCode, text));
        }

        return ExtractText(text);
    }

    /// <summary>
    /// The same call, delivered as it is written.
    ///
    /// A model answer takes several seconds to finish and about one to begin. Waiting for the
    /// whole thing means the panel sits blank through all of it; streaming means the reader
    /// starts reading while the rest is still being written, which is the difference between
    /// "slow" and "working".
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string systemInstruction,
        IReadOnlyList<(string Role, string Text)> turns,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Vertex AI is not configured on this server.");

        var account = LoadAccount();
        var projectId = string.IsNullOrWhiteSpace(_options.ProjectId) ? account.ProjectId : _options.ProjectId;
        var token = await AccessTokenAsync(account, ct).ConfigureAwait(false);

        // alt=sse asks Vertex for server-sent events rather than a JSON array delivered in
        // pieces — the array form arrives as fragments that are not valid JSON on their own.
        var url =
            $"https://{_options.Location}-aiplatform.googleapis.com/v1/projects/{projectId}" +
            $"/locations/{_options.Location}/publishers/google/models/{_options.Model}:streamGenerateContent?alt=sse";

        var body = new
        {
            contents = turns.Select(t => new
            {
                role = t.Role == "model" ? "model" : "user",
                parts = new[] { new { text = t.Text } }
            }).ToArray(),
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            // Same settings as the buffered call, including no thinking budget: this is the
            // path where waiting shows, because the reader is watching the words arrive.
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 4096,
                thinkingConfig = new { thinkingBudget = 0 }
            }
        };

        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // ResponseHeadersRead, or HttpClient buffers the whole body before returning and the
        // streaming above this line is undone by the line that consumes it.
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _log.LogError("Vertex AI refused the stream: {Status} {Body}", (int)response.StatusCode, error);
            throw new InvalidOperationException(FriendlyError(response.StatusCode, error));
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            // Blank lines separate events and "data: [DONE]" ends the stream. Neither carries text.
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var payload = line[5..].Trim();
            if (payload.Length == 0 || payload == "[DONE]") continue;

            string? text = null;
            try
            {
                text = ChunkText(payload);
            }
            catch (JsonException)
            {
                // A malformed event is not worth ending an answer over; the next one usually
                // carries the same text and the reader never sees the gap.
                _log.LogDebug("Skipped an unparseable stream event.");
            }

            if (!string.IsNullOrEmpty(text)) yield return text!;
        }
    }

    private static string? ChunkText(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        if (!candidates[0].TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text)) builder.Append(text.GetString());
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    // -----------------------------------------------------------------
    // Credential
    // -----------------------------------------------------------------

    private ServiceAccount LoadAccount()
    {
        if (_account is not null) return _account;

        var json = !string.IsNullOrWhiteSpace(_options.ServiceAccountJson)
            ? _options.ServiceAccountJson!
            : File.ReadAllText(_options.CredentialsPath!);

        var parsed = JsonSerializer.Deserialize<ServiceAccount>(json, Json)
            ?? throw new InvalidOperationException("The Vertex AI credential could not be read.");

        if (string.IsNullOrWhiteSpace(parsed.ClientEmail) || string.IsNullOrWhiteSpace(parsed.PrivateKey))
            throw new InvalidOperationException("The Vertex AI credential is missing client_email or private_key.");

        _account = parsed;
        return parsed;
    }

    private async Task<string> AccessTokenAsync(ServiceAccount account, CancellationToken ct)
    {
        // A minute of headroom: a token that expires between this check and the call Google
        // receives is a 401 the user reads as "the assistant is broken".
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1)) return _token;

        await _mintLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1)) return _token;

            var assertion = SignedAssertion(account);

            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            using var response = await client.PostAsync(
                account.TokenUri,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion
                }),
                ct).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogError("Google refused the token request: {Status} {Body}", (int)response.StatusCode, body);
                throw new InvalidOperationException(
                    "The Vertex AI credential was refused. Check that the key is current and the service " +
                    "account has the Vertex AI User role.");
            }

            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Google returned no access token.");
            var lifetime = document.RootElement.TryGetProperty("expires_in", out var seconds) ? seconds.GetInt32() : 3600;

            _token = token;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
            return token;
        }
        finally
        {
            _mintLock.Release();
        }
    }

    /// <summary>The self-signed JWT Google exchanges for an access token.</summary>
    private static string SignedAssertion(ServiceAccount account)
    {
        var now = DateTimeOffset.UtcNow;

        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = account.ClientEmail,
            scope = Scope,
            aud = account.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(55).ToUnixTimeSeconds()
        }));

        var payload = Encoding.ASCII.GetBytes($"{header}.{claims}");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.PrivateKey);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{header}.{claims}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // -----------------------------------------------------------------
    // Response
    // -----------------------------------------------------------------

    private static string ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return "The model returned nothing. Try asking again, or more specifically.";

        var first = candidates[0];

        // A blocked answer is not an empty answer, and saying so is the difference between
        // "ask differently" and "this is broken".
        if (first.TryGetProperty("finishReason", out var reason)
            && reason.GetString() is "SAFETY" or "PROHIBITED_CONTENT")
        {
            return "The model declined to answer that.";
        }

        if (!first.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
        {
            return "The model returned nothing usable.";
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text)) builder.Append(text.GetString());
        }

        return builder.Length == 0 ? "The model returned nothing usable." : builder.ToString();
    }

    private static string FriendlyError(System.Net.HttpStatusCode status, string body)
    {
        var detail = body;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                detail = message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON. The raw body is still the most useful thing available.
        }

        return (int)status switch
        {
            403 => $"Vertex AI refused: {detail} The service account needs the Vertex AI User role on this project.",
            404 => $"Vertex AI has no such model in this region: {detail}",
            429 => "Vertex AI is rate limiting this project. Try again in a moment.",
            _ => $"Vertex AI returned an error: {detail}"
        };
    }

    /// <summary>
    /// The fields this needs out of the service-account file. Named explicitly rather than
    /// left to a naming policy: the file is snake_case, and the Web defaults used everywhere
    /// else in the product are camelCase, so every one of these would silently arrive empty.
    /// </summary>
    private sealed class ServiceAccount
    {
        [System.Text.Json.Serialization.JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("client_email")]
        public string ClientEmail { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("private_key")]
        public string PrivateKey { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("token_uri")]
        public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";
    }
}
