using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Gemma4Local.Api.Options;
using Gemma4Local.Api.Services;
using Microsoft.Extensions.Options;

namespace Gemma4Local.Api.Memory;

public sealed class OllamaEmbeddingGenerator(
    IHttpClientFactory httpClientFactory,
    LocalEndpointGuard endpointGuard,
    IOptions<LocalAiOptions> options) : IEmbeddingGenerator
{
    private readonly LocalAiOptions _options = options.Value;

    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        var response = await httpClientFactory.CreateClient("ollama").PostAsJsonAsync("/api/embed", new OllamaEmbedRequest(
            Model: _options.Memory.EmbeddingModel,
            Input: text), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ollama embeddings returned {(int)response.StatusCode}: {error}");
        }

        var embedResponse = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty embedding response.");

        return embedResponse.Embeddings.FirstOrDefault()?.Select(value => (float)value).ToArray()
            ?? throw new InvalidOperationException("Ollama returned no embeddings.");
    }
}

public sealed record OllamaEmbedRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string Input);

public sealed record OllamaEmbedResponse(
    [property: JsonPropertyName("embeddings")] IReadOnlyList<IReadOnlyList<double>> Embeddings);
