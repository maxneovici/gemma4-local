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
    string? Collection,
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

public sealed record MemoryRecordUpdateRequest(
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);

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

public sealed record MemoryRecordDeleteResponse(string Collection, string Id, bool Deleted);

public sealed record MemoryRecordDetail(
    string Id,
    string Collection,
    string Text,
    int TextLength,
    IReadOnlyDictionary<string, string> Metadata);

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

public sealed record MemoryCollectionGroupResponse(
    string Collection,
    int SampledRecords,
    IReadOnlyList<MemoryTenantGroup> Tenants);

public sealed record MemoryTenantGroup(
    string Tenant,
    int Count,
    IReadOnlyList<MemoryCategoryGroup> Categories);

public sealed record MemoryCategoryGroup(string Category, int Count);

public sealed record MemoryGraphRequest(
    string Layer = MemoryLayers.Memory,
    string? Query = null,
    int Limit = 80,
    string? FocusId = null,
    IReadOnlyList<string>? Types = null,
    IReadOnlyDictionary<string, string>? Filter = null);

public sealed record MemoryGraphResponse(
    string Layer,
    string? Query,
    int RecordCount,
    IReadOnlyList<MemoryGraphNode> Nodes,
    IReadOnlyList<MemoryGraphEdge> Edges);

public sealed record MemoryGraphNode(
    string Id,
    string Kind,
    string Label,
    int Weight,
    double X,
    double Y,
    string? RecordId = null,
    string? TextPreview = null,
    string? MemoryType = null,
    string? Provenance = null,
    string? ObservedAt = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MemoryGraphEdge(
    string Source,
    string Target,
    double Weight,
    string Kind);

public sealed record MemoryReviewResponse(
    int PendingCount,
    int DuplicateGroupCount,
    IReadOnlyList<MemoryReviewItem> Pending,
    IReadOnlyList<MemoryDuplicateGroup> DuplicateGroups);

public sealed record MemoryReviewItem(
    string Id,
    string Collection,
    string TextPreview,
    string? MemoryType,
    string? Provenance,
    string? Confidence,
    string? ObservedAt,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MemoryDuplicateGroup(
    string MergeKey,
    string? MemoryType,
    string? Topic,
    IReadOnlyList<MemoryReviewItem> Records);
