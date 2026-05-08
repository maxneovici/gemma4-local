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
            var writes = new List<MemoryUpsertRequest>
            {
                new(
                    Collection: MemoryLayers.Memory,
                    Text: summary,
                    Metadata: MemoryMetadata.Build(new Dictionary<string, string>
                    {
                        ["kind"] = "consolidated_session",
                        ["category"] = "session_summary",
                        ["sessionId"] = session.Id,
                        ["taskGraphId"] = graph?.Id ?? string.Empty
                    }, MemoryLayers.Memory, summary, "session_consolidation", "summary", session.Id, "user", confidence: 0.72))
            };

            writes.AddRange((payload.CoreMemories ?? [])
                .Where(memory => !string.IsNullOrWhiteSpace(memory))
                .Where(memory => !IsNegativeKnowledgeMemory(memory))
                .Select(memory => new MemoryUpsertRequest(
                    Collection: MemoryLayers.Memory,
                    Text: memory.Trim(),
                    Metadata: MemoryMetadata.Build(new Dictionary<string, string>
                    {
                        ["kind"] = "core_memory",
                        ["category"] = "profile",
                        ["sessionId"] = session.Id,
                        ["subject"] = "user",
                    }, MemoryLayers.Memory, memory.Trim(), "session_consolidation", null, session.Id, "user", confidence: 0.76))));

            writes.AddRange((payload.Interests ?? [])
                .Where(interest => !string.IsNullOrWhiteSpace(interest))
                .Where(interest => !IsNegativeKnowledgeMemory(interest))
                .Select(interest => new MemoryUpsertRequest(
                    Collection: MemoryLayers.Memory,
                    Text: interest.Trim(),
                    Metadata: MemoryMetadata.Build(new Dictionary<string, string>
                    {
                        ["kind"] = "interest",
                        ["category"] = "profile",
                        ["sessionId"] = session.Id,
                        ["subject"] = "user",
                    }, MemoryLayers.Memory, interest.Trim(), "session_consolidation", "interest", session.Id, "user", confidence: 0.76))));

            writes.AddRange((payload.OpenLoops ?? [])
                .Where(openLoop => !string.IsNullOrWhiteSpace(openLoop))
                .Select(openLoop => new MemoryUpsertRequest(
                    Collection: MemoryLayers.Memory,
                    Text: openLoop.Trim(),
                    Metadata: MemoryMetadata.Build(new Dictionary<string, string>
                    {
                        ["kind"] = "open_loop",
                        ["category"] = "follow_up",
                        ["sessionId"] = session.Id,
                    }, MemoryLayers.Memory, openLoop.Trim(), "session_consolidation", "goal", session.Id, "assistant", confidence: 0.62, reviewRequired: true))));

            if (graph is not null)
            {
                writes.Add(new MemoryUpsertRequest(
                    Collection: MemoryLayers.Memory,
                    Text: FormatTaskGraphMemory(graph),
                    Metadata: MemoryMetadata.Build(new Dictionary<string, string>
                    {
                        ["kind"] = "task_graph",
                        ["sessionId"] = session.Id,
                        ["taskGraphId"] = graph.Id,
                        ["status"] = graph.Status
                    }, MemoryLayers.Memory, FormatTaskGraphMemory(graph), "task_trace", "summary", session.Id, "assistant", confidence: 0.5, reviewRequired: true)));
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
        var userTranscript = string.Join("\n", session.Messages
            .Where(message => message.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(message => $"user: {message.Content}"));
        var assistantContext = string.Join("\n", session.Messages
            .Where(message => message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .Select(message => $"assistant: {TrimForContext(message.Content)}"));
        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: request.Model ?? runtimeModels.GetCoordinatorModel(),
            Messages:
            [
                new LocalChatMessage("system", "Consolidate this completed local assistant session into durable memory. Return exactly one JSON object with summary, coreMemories, interests, and openLoops. The user is the source of truth. For coreMemories and interests, store only facts, preferences, interests, goals, decisions, and follow-up items explicitly stated or requested by user-authored messages. Assistant/model responses are non-authoritative context only: use them to understand what topic the user was replying to, but never treat assistant claims, guesses, summaries, apologies, refusals, uncertainty, questions, or suggestions as evidence about the user or their family. Never store negative knowledge such as 'no information was provided', 'I don't know', 'I don't have that detail', or 'that was hallucinated' as a profile memory. Do not store secrets, credentials, or sensitive personal data unless explicitly requested. Keep each item concise and retrieval-friendly."),
                new LocalChatMessage("user", $"Task graph context, non-authoritative:\n{graphContext}\n\nUser-authored transcript, authoritative for profile memory:\n{userTranscript}\n\nAssistant response context, non-authoritative and not evidence for profile facts:\n{assistantContext}")
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

    private static string TrimForContext(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private static bool IsNegativeKnowledgeMemory(string text)
    {
        var lower = text.ToLowerInvariant();
        return ContainsAny(lower,
            "no specific information was provided",
            "no specific information",
            "i do not have specific information",
            "i don't have specific information",
            "do not have any specific information",
            "don't have any specific information",
            "not in my current memory",
            "i don't have that detail",
            "i do not have that detail",
            "i don't know",
            "i do not know",
            "must have hallucinated",
            "seems i must have hallucinated");
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string FormatTaskGraphMemory(TaskGraph graph) =>
        $"Goal: {graph.Goal}\nStatus: {graph.Status}\nConfidence: {graph.Confidence:0.00}\nActive node: {graph.ActiveNodeId ?? "none"}\nNodes:\n{string.Join("\n", graph.Nodes.Select(node => $"- {node.Status}: {node.Title}; confidence={node.Confidence:0.00}; blocker={node.Blocker ?? "none"}"))}\nArtifacts:\n{string.Join("\n", graph.Artifacts.Select(artifact => $"- {artifact.Kind}: {artifact.Title}"))}";

    private async Task SaveJobAsync(MemoryConsolidationJob job, CancellationToken cancellationToken)
        => await jobStore.SaveAsync(job, cancellationToken);
}
