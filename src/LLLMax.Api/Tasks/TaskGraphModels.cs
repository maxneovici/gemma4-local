using LLLMax.Api.Agents;

namespace LLLMax.Api.Tasks;

public sealed record TaskGraph(
    string Id,
    string SessionId,
    string Goal,
    string Status,
    string? ActiveNodeId,
    double Confidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TaskGraphNode> Nodes,
    IReadOnlyList<TaskGraphArtifact> Artifacts,
    IReadOnlyList<TaskGraphEvent> Events);

public sealed record TaskGraphNode(
    string Id,
    string Title,
    string Kind,
    string Status,
    string? Blocker = null,
    double Confidence = 0.5,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null);

public sealed record TaskGraphArtifact(
    string Id,
    string Kind,
    string Title,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record TaskGraphEvent(
    string Id,
    string Kind,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record TaskGraphSummary(
    string Id,
    string SessionId,
    string Goal,
    string Status,
    string? ActiveNodeId,
    double Confidence,
    DateTimeOffset UpdatedAt,
    int NodeCount,
    int ArtifactCount);

public sealed record TaskGraphPatch(
    string? Goal = null,
    string? Status = null,
    string? ActiveNodeId = null,
    double? Confidence = null,
    IReadOnlyList<TaskGraphNode>? Nodes = null,
    IReadOnlyList<TaskGraphArtifact>? Artifacts = null,
    IReadOnlyList<TaskGraphEvent>? Events = null);

public sealed record TaskGraphCreateRequest(string SessionId, string Goal);

public sealed record TaskGraphUpdateRequest(string? Status = null, string? ActiveNodeId = null, double? Confidence = null);

public sealed record TaskGraphArtifactRequest(string Kind, string Title, string Content);

public sealed record TaskGraphToolProgress(
    string Tool,
    string Status,
    string Content,
    IReadOnlyDictionary<string, string>? Arguments = null,
    string? Result = null);

public sealed record TaskGraphRunResult(TaskGraph Graph, IReadOnlyList<ReasoningStep> ReasoningSteps);
