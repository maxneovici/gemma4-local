using Gemma4Local.Api.Models;

namespace Gemma4Local.Api.Services;

public interface IOllamaApi
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OllamaModel>> GetModelsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OllamaModelInfo>> GetOpenAiModelsAsync(CancellationToken cancellationToken);

    Task PullModelAsync(string model, CancellationToken cancellationToken);

    Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request, CancellationToken cancellationToken);
}
