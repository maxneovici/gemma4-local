namespace LLLMax.Api.Memory;

public interface IMemoryReflectionService
{
    Task<MemoryReflectionResponse> ReflectAsync(MemoryReflectionRequest request, CancellationToken cancellationToken);

    Task<MemoryProfileResponse> GetProfileAsync(CancellationToken cancellationToken);
}
