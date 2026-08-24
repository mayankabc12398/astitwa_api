using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Extensions.Engine.Data;

/// <summary>
/// Where the assistant's conversations live.
///
/// Ownership is not a parameter here: RepositoryBase stamps the tenant and the signed-in
/// user on every call, and each procedure filters on both. So there is no argument a caller
/// could pass that would read somebody else's thread — the question cannot be asked.
/// </summary>
public sealed class AiThreadRepository : RepositoryBase
{
    /// <summary>
    /// A single message is capped well below MEDIUMTEXT. The editor's contents are already
    /// limited to 60 000 characters by the controller; this stops an answer about them from
    /// being stored many times over as the conversation goes on.
    /// </summary>
    private const int BodyLimit = 20000;

    public AiThreadRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<AiThread?> OpenAsync(string threadKey, string? title, string? language, CancellationToken ct)
        => QuerySingleAsync<AiThread>(
            "sp_ext_ai_thread_open",
            ProcArgs.New()
                .Set("thread_key", threadKey)
                .Set("title", title)
                .Set("language", language),
            ct);

    public Task<IReadOnlyList<AiMessage>> MessagesAsync(string threadKey, int limit, CancellationToken ct)
        => QueryAsync<AiMessage>(
            "sp_ext_ai_thread_messages",
            ProcArgs.New()
                .Set("thread_key", threadKey)
                .Set("limit", limit),
            ct);

    public Task AddAsync(
        string threadKey, string? title, string? language,
        string role, string body, string? model, int? durationMs, CancellationToken ct)
        => ExecuteAsync(
            "sp_ext_ai_message_add",
            ProcArgs.New()
                .Set("thread_key", threadKey)
                .Set("title", title)
                .Set("language", language)
                .Set("role", role)
                .Set("body", Trim(body))
                .Set("model", model)
                .Set("duration_ms", durationMs),
            ct);

    public Task ClearAsync(string threadKey, CancellationToken ct)
        => ExecuteAsync("sp_ext_ai_thread_clear", ProcArgs.New().Set("thread_key", threadKey), ct);

    public Task<PagedResult<AiThread>> ListAsync(PageRequest page, CancellationToken ct)
        => QueryPagedAsync<AiThread>("sp_ext_ai_thread_list", page, ct: ct);

    private static string Trim(string body)
        => body.Length <= BodyLimit ? body : body[..BodyLimit] + "…";
}
