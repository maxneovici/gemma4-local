using LLLMax.Api.Models;
using LLLMax.Api.Services;
using LLLMax.Api.Sessions;
using LLLMax.Api.Tasks;

namespace LLLMax.Api.Memory;

public sealed class MemoryConsolidationService(
    IAssistantSessionStore sessions,
    ITaskGraphService taskGraphs,
    ILocalChatClient chatClient,
    ILocalMemoryStore memoryStore,
    IMemoryConsolidationJobStore jobStore) : IMemoryConsolidationService
{
    public async Task<MemoryConsolidationResponse> ConsolidateSessionAsync(MemoryConsolidationRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new MemoryConsolidationJob(
            Id: Guid.NewGuid().ToString("n"),
            SessionId: request.SessionId,
            Status: "running",
            CreatedAt: now,
            UpdatedAt: now,
            MemoriesWritten: 0);
        await SaveJobAsync(job, cancellationToken);

        try
        {
            var session = await sessions.GetAsync(request.SessionId, cancellationToken);
            var graph = await taskGraphs.GetBySessionAsync(session.Id, cancellationToken);
            var summary = await SummarizeForMemoryAsync(session, graph, cancellationToken);
            var collection = string.IsNullOrWhiteSpace(request.Collection) ? session.Agent : request.Collection.Trim();
            var writes = new List<MemoryUpsertRequest>
            {
                new(
                    Collection: collection,
                    Text: summary,
                    Metadata: new Dictionary<string, string>
                    {
                        ["kind"] = "consolidated_session",
                        ["sessionId"] = session.Id,
                        ["taskGraphId"] = graph?.Id ?? string.Empty
                    })
            };

            if (graph is not null)
            {
                writes.Add(new MemoryUpsertRequest(
                    Collection: collection,
                    Text: FormatTaskGraphMemory(graph),
                    Metadata: new Dictionary<string, string>
                    {
                        ["kind"] = "task_graph",
                        ["sessionId"] = session.Id,
                        ["taskGraphId"] = graph.Id,
                        ["status"] = graph.Status
                    }));
            }

            foreach (var write in writes)
            {
                await memoryStore.UpsertAsync(write, cancellationToken);
            }

            job = job with
            {
                Status = "complete",
                UpdatedAt = DateTimeOffset.UtcNow,
                MemoriesWritten = writes.Count,
                Summary = summary,
                TaskGraphId = graph?.Id
            };
            await SaveJobAsync(job, cancellationToken);

            return new MemoryConsolidationResponse(job, graph);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            job = job with
            {
                Status = "failed",
                UpdatedAt = DateTimeOffset.UtcNow,
                Error = exception.Message
            };
            await SaveJobAsync(job, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<MemoryConsolidationJob>> ListJobsAsync(CancellationToken cancellationToken)
        => await jobStore.ListAsync(cancellationToken);

    private async Task<string> SummarizeForMemoryAsync(AssistantSession session, TaskGraph? graph, CancellationToken cancellationToken)
    {
        var graphContext = graph is null ? "No task graph recorded." : FormatTaskGraphMemory(graph);
        var transcript = string.Join("\n", session.Messages.Select(message => $"{message.Role}: {message.Content}"));
        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: session.Model,
            Messages:
            [
                new LocalChatMessage("system", "Consolidate this completed local assistant session into durable memory. Preserve user goals, decisions, constraints, completed work, blockers, artifacts, and future retrieval keywords. Be concise but specific."),
                new LocalChatMessage("user", $"Task graph:\n{graphContext}\n\nTranscript:\n{transcript}")
            ],
            Temperature: 0.2), cancellationToken);

        return response.Response;
    }

    private static string FormatTaskGraphMemory(TaskGraph graph) =>
        $"Goal: {graph.Goal}\nStatus: {graph.Status}\nConfidence: {graph.Confidence:0.00}\nActive node: {graph.ActiveNodeId ?? "none"}\nNodes:\n{string.Join("\n", graph.Nodes.Select(node => $"- {node.Status}: {node.Title}; confidence={node.Confidence:0.00}; blocker={node.Blocker ?? "none"}"))}\nArtifacts:\n{string.Join("\n", graph.Artifacts.Select(artifact => $"- {artifact.Kind}: {artifact.Title}"))}";

    private async Task SaveJobAsync(MemoryConsolidationJob job, CancellationToken cancellationToken)
        => await jobStore.SaveAsync(job, cancellationToken);
}
