namespace LLLMax.Api.Memory;

public interface ILocalMemoryStore
{
    Task<MemoryUpsertResponse> UpsertAsync(MemoryUpsertRequest request, CancellationToken cancellationToken);

    Task<MemoryBatchUpsertResponse> UpsertBatchAsync(MemoryBatchUpsertRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemorySearchRequest request, CancellationToken cancellationToken);

    Task<MemoryCountResponse> CountAsync(MemoryCountRequest request, CancellationToken cancellationToken);

    Task<MemoryStatsResponse> GetStatsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryCollectionDetail>> ListCollectionsAsync(CancellationToken cancellationToken);

    Task<MemoryCollectionDetail?> GetCollectionAsync(string collection, CancellationToken cancellationToken);

    Task<MemoryCollectionInspectResponse> InspectCollectionAsync(string collection, MemoryCollectionInspectRequest request, CancellationToken cancellationToken);

    Task<MemoryCollectionDeleteResponse> DeleteCollectionAsync(string collection, CancellationToken cancellationToken);
}
