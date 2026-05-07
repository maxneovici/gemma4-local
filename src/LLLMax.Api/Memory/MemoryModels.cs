namespace LLLMax.Api.Memory;

public sealed record MemoryUpsertRequest(
    string Collection,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MemoryBatchUpsertRequest(
    string Collection,
    IReadOnlyList<MemoryUpsertItem> Items);

public sealed record MemoryUpsertItem(
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MemorySearchRequest(
    string Collection,
    string Query,
    int Limit = 5,
    IReadOnlyDictionary<string, string>? Filter = null);

public sealed record MemoryCountRequest(
    string Collection,
    IReadOnlyDictionary<string, string>? Filter = null);

public sealed record MemoryCollectionInspectRequest(
    int Limit = 20,
    string? Cursor = null,
    IReadOnlyDictionary<string, string>? Filter = null);

public sealed record MemoryRecord(
    string Id,
    string Collection,
    string Text,
    float[] Vector,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MemorySearchResult(string Id, string Text, double Score, IReadOnlyDictionary<string, string> Metadata);

public sealed record MemoryUpsertResponse(string Id, string Collection);

public sealed record MemoryBatchUpsertResponse(IReadOnlyList<string> Ids, string Collection);

public sealed record MemoryCountResponse(string Collection, int Count);

public sealed record MemoryStatsResponse(
    string Provider,
    int CollectionCount,
    int RecordCount,
    IReadOnlyList<MemoryCollectionStats> Collections);

public sealed record MemoryCollectionStats(string Name, int RecordCount);

public sealed record MemoryCollectionDetail(
    string Name,
    int RecordCount,
    string Provider,
    string? Status = null,
    int? VectorSize = null,
    string? Distance = null);

public sealed record MemoryCollectionDeleteResponse(string Name, bool Deleted);

public sealed record MemoryCollectionRecordPreview(
    string Id,
    string TextPreview,
    int TextLength,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MemoryCollectionInspectResponse(
    string Collection,
    int Count,
    string? NextCursor,
    IReadOnlyList<MemoryCollectionRecordPreview> Records);
