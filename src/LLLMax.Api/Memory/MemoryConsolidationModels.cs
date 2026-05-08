using LLLMax.Api.Tasks;

namespace LLLMax.Api.Memory;

public sealed record MemoryConsolidationJob(
    string Id,
    string SessionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MemoriesWritten,
    string? Summary = null,
    string? Error = null,
    string? TaskGraphId = null);

public sealed record MemoryConsolidationRequest(string SessionId, string? Collection = null, string? Model = null);

public sealed record MemoryConsolidationResponse(MemoryConsolidationJob Job, TaskGraph? TaskGraph);

public sealed record MemoryConsolidationPayload(
    string Summary,
    IReadOnlyList<MemoryConsolidationItem>? CoreMemories = null,
    IReadOnlyList<MemoryConsolidationItem>? Interests = null,
    IReadOnlyList<MemoryRelationshipItem>? Relationships = null,
    IReadOnlyList<string>? OpenLoops = null);

public sealed record MemoryConsolidationItem(
    string Text,
    string? MemoryType = null,
    string? Subcategory = null,
    string? Topic = null,
    string? Subject = null,
    double? Confidence = null);

public sealed record MemoryRelationshipItem(
    string Text,
    string? Relation = null,
    string? RelatedTo = null,
    string? Topic = null,
    string? Subject = null,
    string? Subcategory = null,
    double? Confidence = null);
