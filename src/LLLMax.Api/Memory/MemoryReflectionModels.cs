namespace LLLMax.Api.Memory;

public sealed record MemoryReflectionRequest(int Limit = 80, string? Model = null);

public sealed record MemoryReflectionResponse(
    string Collection,
    int SourceRecordCount,
    int FactsWritten,
    int CategoriesWritten,
    string Summary);

public sealed record MemoryReflectionPayload(
    string Summary,
    IReadOnlyList<CanonicalProfileFact>? Facts = null,
    IReadOnlyList<MemoryCategorySuggestion>? Categories = null);

public sealed record CanonicalProfileFact(
    string Text,
    string Category = "profile",
    string? Topic = null,
    string Subject = "user",
    double? Confidence = null);

public sealed record MemoryCategorySuggestion(
    string Name,
    string Description,
    IReadOnlyList<string>? Examples = null);

public sealed record MemoryProfileResponse(
    string Collection,
    string Summary,
    int FactCount,
    int CategoryCount,
    DateTimeOffset? ReflectedAt,
    IReadOnlyList<MemoryProfileFact> Facts,
    IReadOnlyList<MemoryProfileCategory> Categories);

public sealed record MemoryProfileFact(
    string Id,
    string Text,
    string Category,
    string? Topic,
    string? Subject,
    double? Confidence,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MemoryProfileCategory(
    string Name,
    int Count,
    string? Description = null);
