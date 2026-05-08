using System.Text.Json;
using System.Text.RegularExpressions;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using LLLMax.Api.Sessions;
using LLLMax.Api.Tasks;
using LLLMax.Api.UserProfile;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Memory;

public sealed partial class MemoryConsolidationService(
    IAssistantSessionStore sessions,
    ITaskGraphService taskGraphs,
    ILocalChatClient chatClient,
    IMemoryWriter memoryWriter,
    IMemoryConsolidationJobStore jobStore,
    IRuntimeModelSettings runtimeModels,
    IFoundationUserProfileStore foundationProfile,
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
                .Where(memory => !string.IsNullOrWhiteSpace(memory.Text))
                .Where(memory => !NormalizeConsolidationMemoryType(memory.MemoryType, memory.Text, memory.Subcategory).Equals("relationship", StringComparison.OrdinalIgnoreCase))
                .Where(memory => !IsNegativeKnowledgeMemory(memory.Text))
                .Select(memory => new MemoryUpsertRequest(
                    Collection: MemoryLayers.Memory,
                    Text: memory.Text.Trim(),
                    Metadata: MemoryMetadata.Build(new Dictionary<string, string>
                    {
                        ["kind"] = "core_memory",
                        ["category"] = "profile",
                        ["subcategory"] = memory.Subcategory?.Trim() ?? string.Empty,
                        ["topic"] = memory.Topic?.Trim() ?? string.Empty,
                        ["sessionId"] = session.Id,
                        ["subject"] = string.IsNullOrWhiteSpace(memory.Subject) ? "user" : memory.Subject.Trim(),
                    }, MemoryLayers.Memory, memory.Text.Trim(), "session_consolidation", NormalizeConsolidationMemoryType(memory.MemoryType, memory.Text, memory.Subcategory), session.Id, "user", confidence: Math.Clamp(memory.Confidence ?? 0.76, 0, 1)))));

            writes.AddRange((payload.Interests ?? [])
                .Where(interest => !string.IsNullOrWhiteSpace(interest.Text))
                .Where(interest => !LooksLikeRelationship(interest.Text))
                .Where(interest => !IsNegativeKnowledgeMemory(interest.Text))
                .Select(interest => new MemoryUpsertRequest(
                    Collection: MemoryLayers.Memory,
                    Text: interest.Text.Trim(),
                    Metadata: MemoryMetadata.Build(new Dictionary<string, string>
                    {
                        ["kind"] = "interest",
                        ["category"] = "profile",
                        ["subcategory"] = string.IsNullOrWhiteSpace(interest.Subcategory) ? "interests" : interest.Subcategory.Trim(),
                        ["topic"] = interest.Topic?.Trim() ?? string.Empty,
                        ["sessionId"] = session.Id,
                        ["subject"] = string.IsNullOrWhiteSpace(interest.Subject) ? "user" : interest.Subject.Trim(),
                    }, MemoryLayers.Memory, interest.Text.Trim(), "session_consolidation", "interest", session.Id, "user", confidence: Math.Clamp(interest.Confidence ?? 0.76, 0, 1)))));

            writes.AddRange((payload.Relationships ?? [])
                .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Text))
                .Where(relationship => !IsNegativeKnowledgeMemory(relationship.Text))
                .Select(relationship =>
                {
                    var metadata = new Dictionary<string, string>
                    {
                        ["kind"] = "canonical_profile_fact",
                        ["category"] = "profile",
                        ["subcategory"] = string.IsNullOrWhiteSpace(relationship.Subcategory) ? "relationships" : relationship.Subcategory.Trim(),
                        ["topic"] = relationship.Topic?.Trim() ?? string.Empty,
                        ["sessionId"] = session.Id,
                        ["subject"] = NormalizeRelationshipSubject(relationship),
                        ["source"] = "session_consolidation"
                    };

                    var relation = string.IsNullOrWhiteSpace(relationship.Relation) ? "related_to" : relationship.Relation.Trim();
                    var relatedTo = NormalizeRelatedTo(relationship.RelatedTo, metadata["subject"]);
                    metadata["relation"] = relation;
                    metadata["relatedTo"] = relatedTo;

                    return new MemoryUpsertRequest(
                        Collection: MemoryLayers.Memory,
                        Text: relationship.Text.Trim(),
                        Metadata: MemoryMetadata.Build(metadata, MemoryLayers.Memory, relationship.Text.Trim(), "session_consolidation", "relationship", session.Id, "user", confidence: Math.Clamp(relationship.Confidence ?? 0.8, 0, 1)));
                }));

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
                if (MemoryWritePolicy.ShouldSkipProfileMemory(write.Text, write.Metadata))
                {
                    continue;
                }

                await memoryWriter.UpsertOrReinforceAsync(write, cancellationToken);
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
        var profile = await foundationProfile.GetAsync(cancellationToken);
        var familyContext = string.IsNullOrWhiteSpace(profile.FamilyAndRelations)
            ? "No explicit foundation family/relations field is set."
            : profile.FamilyAndRelations.Trim();
        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: request.Model ?? runtimeModels.GetCoordinatorModel(),
            Messages:
            [
                new LocalChatMessage("system", "Consolidate this completed local assistant session into durable memory. Return exactly one JSON object with summary, coreMemories, interests, relationships, and openLoops. The user is the source of truth. coreMemories and interests must be arrays of objects with text, memoryType, subcategory, topic, subject, and confidence. relationships must be an array of objects with text, relation, relatedTo, topic, subject, subcategory, and confidence. memoryType is the primary recall axis; category is only secondary metadata and will stay profile. For every user-authored stable fact about family, partners, friends, pets, coworkers, named people, organizations related to the user, or shared activities with named people, write a relationship item with memoryType relationship semantics. Relationship items are canonical profile facts. Assistant/model responses are non-authoritative context only: use them to understand what topic the user was replying to, but never treat assistant claims, guesses, summaries, apologies, refusals, uncertainty, questions, or suggestions as evidence about the user or their family. Never store negative knowledge such as 'no information was provided', 'I don't know', 'I don't have that detail', or 'that was hallucinated' as a profile memory. Do not store secrets, credentials, or sensitive personal data unless explicitly requested. Keep each item concise and retrieval-friendly."),
                new LocalChatMessage("user", $"Foundation family/relations context, explicit and authoritative for resolving who named people are:\n{familyContext}\n\nTask graph context, non-authoritative:\n{graphContext}\n\nUser-authored transcript, authoritative for profile memory:\n{userTranscript}\n\nAssistant response context, non-authoritative and not evidence for profile facts:\n{assistantContext}")
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

    private static string NormalizeConsolidationMemoryType(string? memoryType, string text, string? subcategory = null)
    {
        if (LooksLikeRelationship(text) || LooksLikeRelationship(subcategory ?? string.Empty))
        {
            return "relationship";
        }

        if (!string.IsNullOrWhiteSpace(memoryType))
        {
            var normalized = MemoryMetadata.NormalizeType(memoryType);
            return normalized == "fact" && LooksLikeRelationship(memoryType) ? "relationship" : normalized;
        }

        return "fact";
    }

    private static bool LooksLikeRelationship(string text)
    {
        return RelationshipKeywordRegex().IsMatch(text);
    }

    [GeneratedRegex("\\b(brother|sister|mother|father|parent|spouse|wife|husband|partner|fiance|fiancée|friend|daughter|son|child|coworker|colleague|pet|cat|dog|household|family|relative|relation|relationship|with)\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelationshipKeywordRegex();

    private static string NormalizeRelationshipSubject(MemoryRelationshipItem relationship)
    {
        if (!string.IsNullOrWhiteSpace(relationship.Subject))
        {
            return relationship.Subject.Trim();
        }

        return "user";
    }

    private static string NormalizeRelatedTo(string? relatedTo, string subject)
    {
        if (string.IsNullOrWhiteSpace(relatedTo))
        {
            return "user";
        }

        var trimmed = relatedTo.Trim();
        return trimmed.Equals(subject, StringComparison.OrdinalIgnoreCase) ? "user" : trimmed;
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
