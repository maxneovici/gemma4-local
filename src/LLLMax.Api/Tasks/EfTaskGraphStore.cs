using System.Text.Json;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.Tasks;

public sealed class EfTaskGraphStore(
    IDbContextFactory<LocalDbContext> dbFactory,
    LocalDataPaths paths,
    ILogger<EfTaskGraphStore> logger) : ITaskGraphStore
{
    private const string ImportMarker = "task_graphs_json_imported";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _imported;

    public async Task<TaskGraph> SaveAsync(TaskGraph graph, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.TaskGraphs.SingleOrDefaultAsync(entity => entity.Id == graph.Id, cancellationToken);

            if (existing is null)
            {
                db.TaskGraphs.Add(ToEntity(graph));
            }
            else
            {
                Copy(graph, existing);
            }

            await db.SaveChangesAsync(cancellationToken);
            return graph;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TaskGraph?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TaskGraphs.AsNoTracking().SingleOrDefaultAsync(graph => graph.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<TaskGraph?> GetBySessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TaskGraphs
            .AsNoTracking()
            .Where(graph => graph.SessionId == sessionId)
            .ToListAsync(cancellationToken);
        var graph = entity.OrderByDescending(graph => graph.UpdatedAt).FirstOrDefault();
        return graph is null ? null : ToModel(graph);
    }

    public async Task<IReadOnlyList<TaskGraphSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graphs = await db.TaskGraphs.AsNoTracking().ToListAsync(cancellationToken);

        return graphs.OrderByDescending(graph => graph.UpdatedAt).Select(entity =>
            {
                var graph = ToModel(entity);
                return new TaskGraphSummary(
                    graph.Id,
                    graph.SessionId,
                    graph.Goal,
                    graph.Status,
                    graph.ActiveNodeId,
                    graph.Confidence,
                    graph.UpdatedAt,
                    graph.Nodes.Count,
                    graph.Artifacts.Count);
            })
            .ToList();
    }

    private async Task EnsureImportedAsync(CancellationToken cancellationToken) =>
        await JsonImport.ImportOnceAsync(dbFactory, _gate, () => _imported, () => _imported = true, ImportMarker, ImportJsonAsync, cancellationToken);

    private async Task ImportJsonAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(paths.TaskGraphsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(file);
                var graph = await JsonSerializer.DeserializeAsync<TaskGraph>(stream, JsonOptions, cancellationToken);

                if (graph is null || await db.TaskGraphs.AnyAsync(existing => existing.Id == graph.Id, cancellationToken))
                {
                    continue;
                }

                db.TaskGraphs.Add(ToEntity(graph));
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Could not import task graph file {File}", file);
            }
        }
    }

    private static TaskGraphEntity ToEntity(TaskGraph graph)
    {
        var entity = new TaskGraphEntity();
        Copy(graph, entity);
        return entity;
    }

    private static void Copy(TaskGraph graph, TaskGraphEntity entity)
    {
        entity.Id = graph.Id;
        entity.SessionId = graph.SessionId;
        entity.Goal = graph.Goal;
        entity.Status = graph.Status;
        entity.ActiveNodeId = graph.ActiveNodeId;
        entity.Confidence = graph.Confidence;
        entity.CreatedAt = graph.CreatedAt;
        entity.UpdatedAt = graph.UpdatedAt;
        entity.GraphJson = JsonSerializer.Serialize(graph, JsonOptions);
    }

    private static TaskGraph ToModel(TaskGraphEntity entity) =>
        JsonSerializer.Deserialize<TaskGraph>(entity.GraphJson, JsonOptions)
        ?? new TaskGraph(entity.Id, entity.SessionId, entity.Goal, entity.Status, entity.ActiveNodeId, entity.Confidence, entity.CreatedAt, entity.UpdatedAt, [], [], []);
}
