namespace LLLMax.Api.Memory;

public sealed record MemoryUpsertRequest(
    string Collection,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MemorySearchRequest(
    string Collection,
    string Query,
    int Limit = 5,
    IReadOnlyDictionary<string, string>? Filter = null);

public sealed record MemoryRecord(
    string Id,
    string Collection,
    string Text,
    float[] Vector,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MemorySearchResult(string Id, string Text, double Score, IReadOnlyDictionary<string, string> Metadata);

public sealed record MemoryUpsertResponse(string Id, string Collection);

public sealed record MemoryStatsResponse(
    string Provider,
    int CollectionCount,
    int RecordCount,
    IReadOnlyList<MemoryCollectionStats> Collections);

public sealed record MemoryCollectionStats(string Name, int RecordCount);
