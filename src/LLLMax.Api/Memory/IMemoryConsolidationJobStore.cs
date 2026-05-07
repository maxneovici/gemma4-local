namespace LLLMax.Api.Memory;

public interface IMemoryConsolidationJobStore
{
    Task<MemoryConsolidationJob> SaveAsync(MemoryConsolidationJob job, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryConsolidationJob>> ListAsync(CancellationToken cancellationToken);
}
