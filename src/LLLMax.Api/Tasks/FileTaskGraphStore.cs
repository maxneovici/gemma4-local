using System.Text.Json;
using LLLMax.Api.Services;

namespace LLLMax.Api.Tasks;

public sealed class FileTaskGraphStore(LocalDataPaths paths) : ITaskGraphStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<TaskGraph> SaveAsync(TaskGraph graph, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var stream = File.Create(GetPath(graph.Id));
            await JsonSerializer.SerializeAsync(stream, graph, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return graph;
    }

    public async Task<TaskGraph?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var path = GetPath(id);

        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TaskGraph>(stream, JsonOptions, cancellationToken);
    }

    public async Task<TaskGraph?> GetBySessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        foreach (var graph in await ReadAllAsync(cancellationToken))
        {
            if (graph.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase))
            {
                return graph;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<TaskGraphSummary>> ListAsync(CancellationToken cancellationToken) =>
        (await ReadAllAsync(cancellationToken))
            .OrderByDescending(graph => graph.UpdatedAt)
            .Select(graph => new TaskGraphSummary(
                graph.Id,
                graph.SessionId,
                graph.Goal,
                graph.Status,
                graph.ActiveNodeId,
                graph.Confidence,
                graph.UpdatedAt,
                graph.Nodes.Count,
                graph.Artifacts.Count))
            .ToList();

    private async Task<IReadOnlyList<TaskGraph>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var graphs = new List<TaskGraph>();

        foreach (var file in Directory.EnumerateFiles(paths.TaskGraphsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var graph = await JsonSerializer.DeserializeAsync<TaskGraph>(stream, JsonOptions, cancellationToken);

            if (graph is not null)
            {
                graphs.Add(graph);
            }
        }

        return graphs;
    }

    private string GetPath(string id) => Path.Combine(paths.TaskGraphsDirectory, $"{id}.json");
}
