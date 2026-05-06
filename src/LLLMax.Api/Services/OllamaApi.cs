using System.Net.Http.Json;
using LLLMax.Api.Models;

namespace LLLMax.Api.Services;

public sealed class OllamaApi(IHttpClientFactory httpClientFactory, LocalEndpointGuard endpointGuard) : IOllamaApi
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        try
        {
            var response = await httpClientFactory.CreateClient("ollama").GetAsync("/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<OllamaModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        var response = await httpClientFactory.CreateClient("ollama").GetFromJsonAsync<OllamaModelsResponse>("/api/tags", cancellationToken)
            ?? new OllamaModelsResponse([]);

        return response.Models;
    }

    public async Task<IReadOnlyList<OllamaModelInfo>> GetOpenAiModelsAsync(CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        var response = await httpClientFactory.CreateClient("ollama").GetFromJsonAsync<OpenAiModelsResponse>("/v1/models", cancellationToken)
            ?? new OpenAiModelsResponse([]);

        return response.Data;
    }

    public async Task PullModelAsync(string model, CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        var response = await httpClientFactory.CreateClient("ollama").PostAsJsonAsync("/api/pull", new OllamaPullRequest(
            Model: model,
            Stream: false), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ollama pull returned {(int)response.StatusCode}: {error}");
        }
    }

    public async Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        var response = await httpClientFactory.CreateClient("ollama").PostAsJsonAsync("/api/chat", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ollama returned {(int)response.StatusCode}: {error}");
        }

        return await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty chat response.");
    }

    public async Task<OllamaChatResponse> ChatWithToolsAsync(OllamaNativeToolChatRequest request, CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        var response = await httpClientFactory.CreateClient("ollama").PostAsJsonAsync("/api/chat", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ollama tool chat returned {(int)response.StatusCode}: {error}");
        }

        return await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty tool chat response.");
    }

    public async Task<OllamaChatResponse> ChatVisionAsync(OllamaVisionChatRequest request, CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        var response = await httpClientFactory.CreateClient("ollama").PostAsJsonAsync("/api/chat", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ollama vision chat returned {(int)response.StatusCode}: {error}");
        }

        return await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty vision chat response.");
    }
}
