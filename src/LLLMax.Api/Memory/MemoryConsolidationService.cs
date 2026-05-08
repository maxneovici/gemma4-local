using System.Text.Json;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using LLLMax.Api.Sessions;
using LLLMax.Api.Tasks;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Memory;

public sealed class MemoryConsolidationService(
    IAssistantSessionStore sessions,
    ITaskGraphService taskGraphs,
    ILocalChatClient chatClient,
    ILocalMemoryStore memoryStore,
    IMemoryConsolidationJobStore jobStore,
    IRuntimeModelSettings runtimeModels,
    IOptions<LocalAiOptions> options) : IMemoryConsolidationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalAiOptions _options = options.Value;

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
            var payload = await SummarizeForMemoryAsync(request, session, graph, cancellationToken);
            var summary = payload.Summary;
            var collection = string.IsNullOrWhiteSpace(request.Collection) ? session.Agent : request.Collection.Trim();
            var writes = new List<MemoryUpsertRequest>
            {
                new(
                    Collection: collection,
                    Text: summary,
                    Metadata: new Dictionary<string, string>
                    {
                        ["kind"] = "consolidated_session",
                        ["category"] = "session_summary",
                        ["sessionId"] = session.Id,
                        ["taskGraphId"] = graph?.Id ?? string.Empty,
                        ["observedAt"] = now.ToString("O")
                    })
            };

            writes.AddRange((payload.CoreMemories ?? [])
                .Where(memory => !string.IsNullOrWhiteSpace(memory))
                .Select(memory => new MemoryUpsertRequest(
                    Collection: collection,
                    Text: memory.Trim(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["kind"] = "core_memory",
                        ["category"] = "profile",
                        ["sessionId"] = session.Id,
                        ["subject"] = "user",
                        ["observedAt"] = now.ToString("O")
                    })));

            writes.AddRange((payload.Interests ?? [])
                .Where(interest => !string.IsNullOrWhiteSpace(interest))
                .Select(interest => new MemoryUpsertRequest(
                    Collection: collection,
                    Text: interest.Trim(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["kind"] = "interest",
                        ["category"] = "profile",
                        ["sessionId"] = session.Id,
                        ["subject"] = "user",
                        ["observedAt"] = now.ToString("O")
                    })));

            writes.AddRange((payload.OpenLoops ?? [])
                .Where(openLoop => !string.IsNullOrWhiteSpace(openLoop))
                .Select(openLoop => new MemoryUpsertRequest(
                    Collection: collection,
                    Text: openLoop.Trim(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["kind"] = "open_loop",
                        ["category"] = "follow_up",
                        ["sessionId"] = session.Id,
                        ["observedAt"] = now.ToString("O")
                    })));

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
                        ["status"] = graph.Status,
                        ["observedAt"] = now.ToString("O")
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

    private async Task<MemoryConsolidationPayload> SummarizeForMemoryAsync(MemoryConsolidationRequest request, AssistantSession session, TaskGraph? graph, CancellationToken cancellationToken)
    {
        var graphContext = graph is null ? "No task graph recorded." : FormatTaskGraphMemory(graph);
        var transcript = string.Join("\n", session.Messages.Select(message => $"{message.Role}: {message.Content}"));
        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: request.Model ?? runtimeModels.GetCoordinatorModel(),
            Messages:
            [
                new LocalChatMessage("system", "Consolidate this completed local assistant session into durable memory. Return exactly one JSON object with summary, coreMemories, interests, and openLoops. Store only facts, preferences, interests, goals, decisions, and follow-up items explicitly stated or requested by the user. Do not turn assistant-generated suggestions, questions, jokes, or speculative inferences into memories. Do not store secrets, credentials, or sensitive personal data unless explicitly requested. Keep each item concise and retrieval-friendly."),
                new LocalChatMessage("user", $"Task graph:\n{graphContext}\n\nTranscript:\n{transcript}")
            ],
            Temperature: 0.2), cancellationToken);

        return ParsePayload(response.Response);
    }

    private static MemoryConsolidationPayload ParsePayload(string text)
    {
        var json = ExtractJsonObject(text);

        if (json is not null)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<MemoryConsolidationPayload>(json, JsonOptions);

                if (payload is not null && !string.IsNullOrWhiteSpace(payload.Summary))
                {
                    return payload;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new MemoryConsolidationPayload(text.Trim());
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string FormatTaskGraphMemory(TaskGraph graph) =>
        $"Goal: {graph.Goal}\nStatus: {graph.Status}\nConfidence: {graph.Confidence:0.00}\nActive node: {graph.ActiveNodeId ?? "none"}\nNodes:\n{string.Join("\n", graph.Nodes.Select(node => $"- {node.Status}: {node.Title}; confidence={node.Confidence:0.00}; blocker={node.Blocker ?? "none"}"))}\nArtifacts:\n{string.Join("\n", graph.Artifacts.Select(artifact => $"- {artifact.Kind}: {artifact.Title}"))}";

    private async Task SaveJobAsync(MemoryConsolidationJob job, CancellationToken cancellationToken)
        => await jobStore.SaveAsync(job, cancellationToken);
}
