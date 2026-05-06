using LLLMax.Api.Models;

namespace LLLMax.Api.Services;

public interface IOllamaApi
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OllamaModel>> GetModelsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OllamaModelInfo>> GetOpenAiModelsAsync(CancellationToken cancellationToken);

    Task PullModelAsync(string model, CancellationToken cancellationToken);

    Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request, CancellationToken cancellationToken);

    Task<OllamaChatResponse> ChatWithToolsAsync(OllamaNativeToolChatRequest request, CancellationToken cancellationToken);

    Task<OllamaChatResponse> ChatVisionAsync(OllamaVisionChatRequest request, CancellationToken cancellationToken);
}
