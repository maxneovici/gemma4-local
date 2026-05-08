namespace LLLMax.Api.Memory;

public interface IMemoryWriter
{
    Task<MemoryUpsertResponse> UpsertOrReinforceAsync(MemoryUpsertRequest request, CancellationToken cancellationToken);

    Task<MemoryBatchUpsertResponse> UpsertOrReinforceBatchAsync(MemoryBatchUpsertRequest request, CancellationToken cancellationToken);
}
