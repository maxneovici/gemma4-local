namespace LLLMax.Api.Memory;

public interface ILocalMemoryStore
{
    Task<MemoryUpsertResponse> UpsertAsync(MemoryUpsertRequest request, CancellationToken cancellationToken);

    Task<MemoryBatchUpsertResponse> UpsertBatchAsync(MemoryBatchUpsertRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemorySearchRequest request, CancellationToken cancellationToken);

    Task<MemoryCountResponse> CountAsync(MemoryCountRequest request, CancellationToken cancellationToken);

    Task<MemoryStatsResponse> GetStatsAsync(CancellationToken cancellationToken);
}
