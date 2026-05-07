using System.Text.Json;
using LLLMax.Api.Agents;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using LLLMax.Api.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Sessions;

public sealed class EfAssistantSessionStore(
    IDbContextFactory<LocalDbContext> dbFactory,
    LocalDataPaths paths,
    IOptions<LocalAiOptions> options,
    ILogger<EfAssistantSessionStore> logger) : IAssistantSessionStore
{
    private const string ImportMarker = "sessions_json_imported";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly LocalAiOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _imported;

    public async Task<AssistantSession> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
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
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var sessions = await db.Sessions
            .AsNoTracking()
            .Where(session => session.Messages.Any())
            .Select(session => new SessionListResponse(
                session.Id,
                session.Title,
                session.Agent,
                session.Model,
                session.UpdatedAt,
                session.Messages.Count,
                session.Summary))
            .ToListAsync(cancellationToken);

        return sessions.OrderByDescending(session => session.UpdatedAt).ToList();
    }

    public async Task<AssistantSession> GetAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await GetAsync(db, id, cancellationToken);
    }

    public async Task SaveAsync(AssistantSession session, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var persisted = await TryGetAsync(db, session.Id, cancellationToken);
            var merged = persisted is null ? session : MergeMessages(session, persisted);
            await SaveUnsafeAsync(db, merged, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendMessageAsync(string id, LocalChatMessage message, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var session = await GetAsync(db, id, cancellationToken);
            await SaveUnsafeAsync(db, session with
            {
                Messages = [.. session.Messages, message],
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var affected = await db.Sessions.Where(session => session.Id == id).ExecuteDeleteAsync(cancellationToken);
        return affected > 0;
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sessions.ExecuteDeleteAsync(cancellationToken);
    }

    private async Task EnsureImportedAsync(CancellationToken cancellationToken)
    {
        await JsonImport.ImportOnceAsync(
            dbFactory,
            _gate,
            () => _imported,
            () => _imported = true,
            ImportMarker,
            ImportJsonSessionsAsync,
            cancellationToken);
    }

    private async Task ImportJsonSessionsAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(paths.SessionsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(file);
                var session = await JsonSerializer.DeserializeAsync<AssistantSession>(stream, JsonOptions, cancellationToken);

                if (session is null || session.Messages.Count == 0 || await db.Sessions.AnyAsync(existing => existing.Id == session.Id, cancellationToken))
                {
                    continue;
                }

                AddSession(db, session);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Could not import session file {File}", file);
            }
        }
    }

    private static async Task<AssistantSession> GetAsync(LocalDbContext db, string id, CancellationToken cancellationToken) =>
        await TryGetAsync(db, id, cancellationToken) ?? throw new InvalidOperationException($"Session '{id}' does not exist.");

    private static async Task<AssistantSession?> TryGetAsync(LocalDbContext db, string id, CancellationToken cancellationToken)
    {
        var entity = await db.Sessions
            .AsNoTracking()
            .Include(session => session.Messages)
            .SingleOrDefaultAsync(session => session.Id == id, cancellationToken);

        return entity is null ? null : ToModel(entity);
    }

    private static async Task SaveUnsafeAsync(LocalDbContext db, AssistantSession session, CancellationToken cancellationToken)
    {
        var existing = await db.Sessions
            .Include(entity => entity.Messages)
            .SingleOrDefaultAsync(entity => entity.Id == session.Id, cancellationToken);

        if (existing is null)
        {
            AddSession(db, session);
        }
        else
        {
            existing.Title = session.Title;
            existing.Agent = session.Agent;
            existing.Model = session.Model;
            existing.Summary = session.Summary;
            existing.CreatedAt = session.CreatedAt;
            existing.UpdatedAt = session.UpdatedAt;
            existing.Messages.Clear();
            existing.Messages.AddRange(session.Messages.Select((message, index) => ToEntity(session.Id, index, message)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddSession(LocalDbContext db, AssistantSession session) =>
        db.Sessions.Add(new SessionEntity
        {
            Id = session.Id,
            Title = session.Title,
            Agent = session.Agent,
            Model = session.Model,
            Summary = session.Summary,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            Messages = session.Messages.Select((message, index) => ToEntity(session.Id, index, message)).ToList()
        });

    private static SessionMessageEntity ToEntity(string sessionId, int ordinal, LocalChatMessage message) =>
        new()
        {
            SessionId = sessionId,
            Ordinal = ordinal,
            Role = message.Role,
            Content = message.Content,
            TraceId = message.TraceId,
            ReasoningStepsJson = message.ReasoningSteps is null ? null : JsonSerializer.Serialize(message.ReasoningSteps, JsonOptions),
            TaskGraphJson = message.TaskGraph is null ? null : JsonSerializer.Serialize(message.TaskGraph, JsonOptions)
        };

    private static AssistantSession ToModel(SessionEntity entity) =>
        new(
            Id: entity.Id,
            Title: entity.Title,
            Agent: entity.Agent,
            Model: entity.Model,
            Summary: entity.Summary,
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt,
            Messages: entity.Messages
                .OrderBy(message => message.Ordinal)
                .Select(message => new LocalChatMessage(
                    Role: message.Role,
                    Content: message.Content,
                    TraceId: message.TraceId,
                    ReasoningSteps: message.ReasoningStepsJson is null ? null : JsonSerializer.Deserialize<IReadOnlyList<ReasoningStep>>(message.ReasoningStepsJson, JsonOptions),
                    TaskGraph: message.TaskGraphJson is null ? null : JsonSerializer.Deserialize<TaskGraph>(message.TaskGraphJson, JsonOptions)))
                .ToList());

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

        return outOfBandMessages.Count > 0
            ? incoming with
            {
                Messages = [.. incoming.Messages, .. outOfBandMessages],
                UpdatedAt = Max(incoming.UpdatedAt, persisted.UpdatedAt)
            }
            : incoming;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static string MessageKey(LocalChatMessage message) => $"{message.Role}\u001f{message.Content}";
}
