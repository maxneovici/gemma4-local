using System.Text.Json;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Sessions;

public sealed class FileAssistantSessionStore(LocalDataPaths paths, IOptions<LocalAiOptions> options) : IAssistantSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalAiOptions _options = options.Value;

    public async Task<AssistantSession> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new AssistantSession(
            Id: Guid.NewGuid().ToString("n"),
            Title: string.IsNullOrWhiteSpace(request.Title) ? "New LLLMax session" : request.Title.Trim(),
            Agent: string.IsNullOrWhiteSpace(request.Agent) ? _options.Orchestration.DefaultAgent : request.Agent.Trim(),
            Model: request.Model,
            Summary: null,
            CreatedAt: now,
            UpdatedAt: now,
            Messages: []);

        await SaveAsync(session, cancellationToken);

        return session;
    }

    public async Task<IReadOnlyList<SessionListResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var sessions = new List<SessionListResponse>();

        foreach (var file in Directory.EnumerateFiles(paths.SessionsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var session = await JsonSerializer.DeserializeAsync<AssistantSession>(stream, JsonOptions, cancellationToken);

            if (session is not null)
            {
                sessions.Add(new SessionListResponse(
                    session.Id,
                    session.Title,
                    session.Agent,
                    session.Model,
                    session.UpdatedAt,
                    session.Messages.Count,
                    session.Summary));
            }
        }

        return sessions.OrderByDescending(session => session.UpdatedAt).ToList();
    }

    public async Task<AssistantSession> GetAsync(string id, CancellationToken cancellationToken)
    {
        var file = GetPath(id);

        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"Session '{id}' does not exist.");
        }

        await using var stream = File.OpenRead(file);

        return await JsonSerializer.DeserializeAsync<AssistantSession>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{id}' could not be read.");
    }

    public async Task SaveAsync(AssistantSession session, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(GetPath(session.Id));
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
    }

    private string GetPath(string id) => Path.Combine(paths.SessionsDirectory, $"{id}.json");
}
