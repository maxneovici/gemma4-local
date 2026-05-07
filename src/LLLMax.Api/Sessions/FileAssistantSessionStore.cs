using System.Text.Json;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Sessions;

public sealed class FileAssistantSessionStore(LocalDataPaths paths, IOptions<LocalAiOptions> options) : IAssistantSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalAiOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

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

            if (session is { Messages.Count: > 0 })
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
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var merged = File.Exists(GetPath(session.Id))
                ? MergeMessages(session, await GetUnsafeAsync(session.Id, cancellationToken))
                : session;
            await SaveUnsafeAsync(merged, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendMessageAsync(string id, LocalChatMessage message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var session = await GetUnsafeAsync(id, cancellationToken);
            var messages = session.Messages.Concat([message]).ToList();
            await SaveUnsafeAsync(session with { Messages = messages, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = GetPath(id);

        if (!File.Exists(file))
        {
            return Task.FromResult(false);
        }

        File.Delete(file);
        return Task.FromResult(true);
    }

    private async Task SaveUnsafeAsync(AssistantSession session, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(GetPath(session.Id));
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
    }

    private static AssistantSession MergeMessages(AssistantSession incoming, AssistantSession persisted)
    {
        if (persisted.Messages.Count == 0)
        {
            return incoming;
        }

        if (incoming.Messages.SequenceEqual(persisted.Messages.Take(incoming.Messages.Count)))
        {
            return incoming with
            {
                Messages = [.. incoming.Messages, .. persisted.Messages.Skip(incoming.Messages.Count)],
                UpdatedAt = Max(incoming.UpdatedAt, persisted.UpdatedAt)
            };
        }

        var incomingKeys = incoming.Messages.Select(MessageKey).ToHashSet(StringComparer.Ordinal);
        var outOfBandMessages = persisted.Messages
            .Where(message => !incomingKeys.Contains(MessageKey(message)))
            .ToList();

        if (outOfBandMessages.Count > 0)
        {
            return incoming with
            {
                Messages = [.. incoming.Messages, .. outOfBandMessages],
                UpdatedAt = Max(incoming.UpdatedAt, persisted.UpdatedAt)
            };
        }

        return incoming;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static string MessageKey(LocalChatMessage message) => $"{message.Role}\u001f{message.Content}";

    private async Task<AssistantSession> GetUnsafeAsync(string id, CancellationToken cancellationToken)
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

    public Task DeleteAllAsync(CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(paths.SessionsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string id) => Path.Combine(paths.SessionsDirectory, $"{id}.json");
}
