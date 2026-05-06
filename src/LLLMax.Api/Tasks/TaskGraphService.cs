using System.Text.Json;
using LLLMax.Api.Agents;

namespace LLLMax.Api.Tasks;

public sealed class TaskGraphService(ITaskGraphStore store) : ITaskGraphService
{
    public async Task<TaskGraph> EnsureForSessionAsync(string sessionId, string goal, CancellationToken cancellationToken)
    {
        var existing = await store.GetBySessionAsync(sessionId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var graph = new TaskGraph(
            Id: NewId(),
            SessionId: sessionId,
            Goal: goal,
            Status: "active",
            ActiveNodeId: null,
            Confidence: 0.35,
            CreatedAt: now,
            UpdatedAt: now,
            Nodes: [],
            Artifacts: [],
            Events: [NewEvent("goal", goal)]);

        return await store.SaveAsync(graph, cancellationToken);
    }

    public Task<TaskGraph?> GetBySessionAsync(string sessionId, CancellationToken cancellationToken) =>
        store.GetBySessionAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<TaskGraphSummary>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public async Task<TaskGraph> UpdateAsync(string graphId, TaskGraphUpdateRequest request, CancellationToken cancellationToken)
    {
        var graph = await GetRequiredAsync(graphId, cancellationToken);

        return await SaveAsync(graph with
        {
            Status = request.Status ?? graph.Status,
            ActiveNodeId = request.ActiveNodeId ?? graph.ActiveNodeId,
            Confidence = ClampConfidence(request.Confidence ?? graph.Confidence),
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task<TaskGraph> AddArtifactAsync(string graphId, TaskGraphArtifactRequest request, CancellationToken cancellationToken)
    {
        var graph = await GetRequiredAsync(graphId, cancellationToken);
        var artifact = NewArtifact(request.Kind, request.Title, request.Content);

        return await SaveAsync(graph with
        {
            Artifacts = [.. graph.Artifacts, artifact],
            Events = [.. graph.Events, NewEvent("artifact", $"{artifact.Kind}: {artifact.Title}")],
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task<TaskGraph> RecordRunStartedAsync(string sessionId, string goal, CancellationToken cancellationToken)
    {
        var graph = await EnsureForSessionAsync(sessionId, goal, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var nodes = graph.Nodes.Select(node => node.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
            ? node with { Status = "complete", Confidence = Math.Max(node.Confidence, 0.65), CompletedAt = node.CompletedAt ?? now, Blocker = null }
            : node).ToList();
        var node = new TaskGraphNode(NewId(), goal, "turn", "active", Confidence: 0.35, StartedAt: now);
        nodes.Add(node);

        if (!graph.Goal.Equals(goal, StringComparison.OrdinalIgnoreCase))
        {
            graph = graph with { Goal = goal };
        }

        return await SaveAsync(graph with
        {
            Status = "active",
            ActiveNodeId = node.Id,
            Confidence = 0.35,
            Nodes = nodes,
            Events = [.. graph.Events, NewEvent("run_started", goal)],
            UpdatedAt = now
        }, cancellationToken);
    }

    public async Task<TaskGraph> RecordToolProgressAsync(string sessionId, TaskGraphToolProgress progress, CancellationToken cancellationToken)
    {
        var graph = await EnsureForSessionAsync(sessionId, progress.Tool, cancellationToken);
        var title = $"Tool {progress.Tool}";
        var toolNode = graph.Nodes.LastOrDefault(node => node.Kind == "tool" && node.Title.Equals(title, StringComparison.OrdinalIgnoreCase) && node.Status != "complete");
        var node = toolNode ?? new TaskGraphNode(NewId(), title, "tool", progress.Status, Confidence: 0.45, StartedAt: DateTimeOffset.UtcNow);
        var nodes = graph.Nodes.Where(existing => existing.Id != node.Id).ToList();
        var nextNode = node with
        {
            Status = progress.Status,
            Blocker = progress.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase) ? progress.Content : null,
            Confidence = progress.Status.Equals("complete", StringComparison.OrdinalIgnoreCase) ? 0.8 : node.Confidence,
            CompletedAt = progress.Status.Equals("complete", StringComparison.OrdinalIgnoreCase) ? DateTimeOffset.UtcNow : node.CompletedAt
        };
        nodes.Add(nextNode);
        var artifacts = graph.Artifacts.ToList();
        if (progress.Result is not null)
        {
            artifacts.RemoveAll(artifact => artifact.Kind == "tool_result" && artifact.Title.Equals(progress.Tool, StringComparison.OrdinalIgnoreCase));
            artifacts.Add(NewArtifact("tool_result", progress.Tool, progress.Result));
        }
        var eventContent = progress.Arguments is null
            ? progress.Content
            : $"{progress.Content}\n{JsonSerializer.Serialize(progress.Arguments)}";

        var activeNodeId = progress.Status.Equals("complete", StringComparison.OrdinalIgnoreCase)
            ? nodes.LastOrDefault(existing => existing.Kind == "turn" && existing.Status == "active")?.Id
            : nextNode.Id;

        return await SaveAsync(graph with
        {
            Status = progress.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase) ? "blocked" : "active",
            ActiveNodeId = activeNodeId,
            Nodes = nodes,
            Artifacts = artifacts,
            Events = [.. graph.Events, NewEvent($"tool_{progress.Status}", eventContent)],
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task<TaskGraph> RecordRunCompletedAsync(string sessionId, string answer, IReadOnlyList<ReasoningStep> reasoningSteps, CancellationToken cancellationToken)
    {
        var graph = await EnsureForSessionAsync(sessionId, "Session task", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var nodes = graph.Nodes.Select(node => node.Id == graph.ActiveNodeId
            ? node with { Status = "complete", Confidence = 0.85, CompletedAt = now, Blocker = null }
            : node).ToList();
        var artifacts = graph.Artifacts.ToList();

        if (!string.IsNullOrWhiteSpace(answer))
        {
            artifacts.RemoveAll(artifact => artifact.Kind == "answer");
            artifacts.Add(NewArtifact("answer", "Final answer", answer));
        }

        if (reasoningSteps.Count > 0)
        {
            artifacts.RemoveAll(artifact => artifact.Kind == "trace");
            artifacts.Add(NewArtifact("trace", "Reasoning trace", string.Join("\n", reasoningSteps.Select(step => $"{step.Kind}: {step.Content}"))));
        }

        return await SaveAsync(graph with
        {
            Status = "complete",
            ActiveNodeId = null,
            Confidence = Math.Max(graph.Confidence, 0.8),
            Nodes = nodes,
            Artifacts = artifacts,
            Events = [.. graph.Events, NewEvent("run_completed", "Assistant response completed.")],
            UpdatedAt = now
        }, cancellationToken);
    }

    public async Task<TaskGraph> RecordRunBlockedAsync(string sessionId, string blocker, CancellationToken cancellationToken)
    {
        var graph = await EnsureForSessionAsync(sessionId, "Session task", cancellationToken);
        var nodes = graph.Nodes.Select(node => node.Id == graph.ActiveNodeId
            ? node with { Status = "blocked", Blocker = blocker, Confidence = Math.Min(node.Confidence, 0.35) }
            : node).ToList();

        return await SaveAsync(graph with
        {
            Status = "blocked",
            Confidence = Math.Min(graph.Confidence, 0.35),
            Nodes = nodes,
            Events = [.. graph.Events, NewEvent("blocked", blocker)],
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    private async Task<TaskGraph> GetRequiredAsync(string graphId, CancellationToken cancellationToken) =>
        await store.GetAsync(graphId, cancellationToken)
        ?? throw new InvalidOperationException($"Task graph '{graphId}' does not exist.");

    private Task<TaskGraph> SaveAsync(TaskGraph graph, CancellationToken cancellationToken) =>
        store.SaveAsync(graph with
        {
            Confidence = ClampConfidence(graph.Confidence),
            Nodes = graph.Nodes.Select(node => string.IsNullOrWhiteSpace(node.Kind) ? node with { Kind = "turn" } : node).ToList()
        }, cancellationToken);

    private static TaskGraphEvent NewEvent(string kind, string content) =>
        new(NewId(), kind, content, DateTimeOffset.UtcNow);

    private static TaskGraphArtifact NewArtifact(string kind, string title, string content) =>
        new(NewId(), kind, title, content, DateTimeOffset.UtcNow);

    private static string NewId() => Guid.NewGuid().ToString("n");

    private static double ClampConfidence(double confidence) => Math.Clamp(Math.Round(confidence, 2), 0, 1);
}
