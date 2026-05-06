namespace LLLMax.Api.Memory;

public interface IMemoryConsolidationService
{
    Task<MemoryConsolidationResponse> ConsolidateSessionAsync(MemoryConsolidationRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryConsolidationJob>> ListJobsAsync(CancellationToken cancellationToken);
}
