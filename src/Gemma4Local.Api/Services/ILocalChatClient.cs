using Gemma4Local.Api.Models;

namespace Gemma4Local.Api.Services;

public interface ILocalChatClient
{
    Task<LocalChatResponse> ChatAsync(LocalChatRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<LocalModelResponse>> GetModelsAsync(CancellationToken cancellationToken);
}
