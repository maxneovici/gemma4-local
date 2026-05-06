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

public sealed record MemoryConsolidationRequest(string SessionId, string? Collection = null);

public sealed record MemoryConsolidationResponse(MemoryConsolidationJob Job, TaskGraph? TaskGraph);
