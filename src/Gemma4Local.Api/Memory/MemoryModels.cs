namespace Gemma4Local.Api.Memory;

public sealed record MemoryUpsertRequest(
    string Collection,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MemorySearchRequest(string Collection, string Query, int Limit = 5);

public sealed record MemoryRecord(
    string Id,
    string Collection,
    string Text,
    float[] Vector,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MemorySearchResult(string Id, string Text, double Score, IReadOnlyDictionary<string, string> Metadata);

public sealed record MemoryUpsertResponse(string Id, string Collection);
