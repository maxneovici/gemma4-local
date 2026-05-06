using LLLMax.Api.Models;

namespace LLLMax.Api.Services;

public interface ILocalChatClient
{
    Task<LocalChatResponse> ChatAsync(LocalChatRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<LocalChatStreamChunk> StreamChatAsync(LocalChatRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<LocalModelResponse>> GetModelsAsync(CancellationToken cancellationToken);
}
