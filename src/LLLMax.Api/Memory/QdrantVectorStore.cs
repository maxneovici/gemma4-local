using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Memory;

public sealed class QdrantVectorStore(
    IHttpClientFactory httpClientFactory,
    IEmbeddingGenerator embeddingGenerator,
    IOptions<LocalAiOptions> options) : ILocalMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly LocalAiOptions _options = options.Value;

    public async Task<MemoryUpsertResponse> UpsertAsync(MemoryUpsertRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Collection))
        {
            throw new ArgumentException("Collection is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Text is required.", nameof(request));
        }

        var collection = NormalizeCollection(request.Collection);
        var vector = await embeddingGenerator.GenerateAsync(request.Text, cancellationToken);
        await EnsureCollectionAsync(collection, vector.Length, cancellationToken);

        var id = Guid.NewGuid().ToString("D");
        var response = await Client.PutAsJsonAsync($"/collections/{Uri.EscapeDataString(collection)}/points?wait=true", new QdrantUpsertRequest(
        [
            new QdrantPoint(
                Id: id,
                Vector: vector,
                Payload: new Dictionary<string, object?>
                {
                    ["collection"] = request.Collection,
                    ["text"] = request.Text,
                    ["metadata"] = request.Metadata ?? new Dictionary<string, string>()
                })
        ]), JsonOptions, cancellationToken);

        await ThrowIfFailedAsync(response, "Qdrant point upsert", cancellationToken);
        return new MemoryUpsertResponse(id, request.Collection);
    }

    public async Task<MemoryBatchUpsertResponse> UpsertBatchAsync(MemoryBatchUpsertRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Collection))
        {
            throw new ArgumentException("Collection is required.", nameof(request));
        }

        var items = request.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .ToList();

        if (items.Count == 0)
        {
            return new MemoryBatchUpsertResponse([], request.Collection);
        }

        var collection = NormalizeCollection(request.Collection);
        var embeddings = new List<float[]>(items.Count);

        foreach (var item in items)
        {
            embeddings.Add(await embeddingGenerator.GenerateAsync(item.Text, cancellationToken));
        }

        await EnsureCollectionAsync(collection, embeddings[0].Length, cancellationToken);

        var ids = items.Select(_ => Guid.NewGuid().ToString("D")).ToList();
        var points = items.Select((item, index) => new QdrantPoint(
            Id: ids[index],
            Vector: embeddings[index],
            Payload: new Dictionary<string, object?>
            {
                ["collection"] = request.Collection,
                ["text"] = item.Text,
                ["metadata"] = item.Metadata ?? new Dictionary<string, string>()
            })).ToList();
        var response = await Client.PutAsJsonAsync($"/collections/{Uri.EscapeDataString(collection)}/points?wait=true", new QdrantUpsertRequest(points), JsonOptions, cancellationToken);

        await ThrowIfFailedAsync(response, "Qdrant batch point upsert", cancellationToken);
        return new MemoryBatchUpsertResponse(ids, request.Collection);
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemorySearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Collection) || string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var collection = NormalizeCollection(request.Collection);

        if (!await CollectionExistsAsync(collection, cancellationToken))
        {
            return [];
        }

        var queryVector = await embeddingGenerator.GenerateAsync(request.Query, cancellationToken);
        var response = await Client.PostAsJsonAsync($"/collections/{Uri.EscapeDataString(collection)}/points/search", new QdrantSearchRequest(
            Vector: queryVector,
            Limit: Math.Clamp(request.Limit, 1, 50),
            WithPayload: true,
            Filter: BuildFilter(request.Filter)), JsonOptions, cancellationToken);

        await ThrowIfFailedAsync(response, "Qdrant vector search", cancellationToken);
        var search = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Qdrant returned an empty search response.");

        return search.Result
            .Select(point => new MemorySearchResult(
                Id: point.Id,
                Text: point.Payload.TryGetString("text") ?? string.Empty,
                Score: point.Score,
                Metadata: point.Payload.TryGetMetadata()))
            .ToList();
    }

    public async Task<MemoryCountResponse> CountAsync(MemoryCountRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Collection))
        {
            return new MemoryCountResponse(request.Collection, 0);
        }

        var collection = NormalizeCollection(request.Collection);

        if (!await CollectionExistsAsync(collection, cancellationToken))
        {
            return new MemoryCountResponse(request.Collection, 0);
        }

        var response = await Client.PostAsJsonAsync($"/collections/{Uri.EscapeDataString(collection)}/points/count", new QdrantCountRequest(
            Exact: true,
            Filter: BuildFilter(request.Filter)), JsonOptions, cancellationToken);
        await ThrowIfFailedAsync(response, $"Qdrant count for {collection}", cancellationToken);
        var count = await response.Content.ReadFromJsonAsync<QdrantCountResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Qdrant returned an empty count response.");

        return new MemoryCountResponse(request.Collection, count.Result.Count);
    }

    public async Task<MemoryStatsResponse> GetStatsAsync(CancellationToken cancellationToken)
    {
        var response = await Client.GetAsync("/collections", cancellationToken);
        await ThrowIfFailedAsync(response, "Qdrant collection list", cancellationToken);
        var collections = await response.Content.ReadFromJsonAsync<QdrantCollectionsResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Qdrant returned an empty collections response.");
        var stats = new List<MemoryCollectionStats>();

        foreach (var collection in collections.Result.Collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var countResponse = await Client.PostAsJsonAsync($"/collections/{Uri.EscapeDataString(collection.Name)}/points/count", new QdrantCountRequest(Exact: true), JsonOptions, cancellationToken);
            await ThrowIfFailedAsync(countResponse, $"Qdrant count for {collection.Name}", cancellationToken);
            var count = await countResponse.Content.ReadFromJsonAsync<QdrantCountResponse>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Qdrant returned an empty count response.");
            stats.Add(new MemoryCollectionStats(collection.Name, count.Result.Count));
        }

        stats = stats.OrderBy(collection => collection.Name).ToList();

        return new MemoryStatsResponse(
            Provider: "Qdrant",
            CollectionCount: stats.Count,
            RecordCount: stats.Sum(collection => collection.RecordCount),
            Collections: stats);
    }

    private async Task EnsureCollectionAsync(string collection, int vectorSize, CancellationToken cancellationToken)
    {
        if (await CollectionExistsAsync(collection, cancellationToken))
        {
            return;
        }

        var response = await Client.PutAsJsonAsync($"/collections/{Uri.EscapeDataString(collection)}", new QdrantCreateCollectionRequest(new QdrantVectorParams(vectorSize, "Cosine")), JsonOptions, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return;
        }

        await ThrowIfFailedAsync(response, $"Qdrant collection create for {collection}", cancellationToken);
    }

    private async Task<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken)
    {
        var response = await Client.GetAsync($"/collections/{Uri.EscapeDataString(collection)}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        await ThrowIfFailedAsync(response, $"Qdrant collection lookup for {collection}", cancellationToken);
        return true;
    }

    private HttpClient Client => httpClientFactory.CreateClient("qdrant");

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{operation} returned {(int)response.StatusCode}: {error}");
    }

    private static string NormalizeCollection(string collection)
    {
        var safe = new string(collection.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? "default" : safe;
    }

    private static QdrantFilter? BuildFilter(IReadOnlyDictionary<string, string>? filter)
    {
        if (filter is null || filter.Count == 0)
        {
            return null;
        }

        return new QdrantFilter(filter
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new QdrantFieldCondition($"metadata.{pair.Key}", new QdrantMatchValue(pair.Value)))
            .ToList());
    }

    private sealed record QdrantCreateCollectionRequest([property: JsonPropertyName("vectors")] QdrantVectorParams Vectors);

    private sealed record QdrantVectorParams([property: JsonPropertyName("size")] int Size, [property: JsonPropertyName("distance")] string Distance);

    private sealed record QdrantUpsertRequest([property: JsonPropertyName("points")] IReadOnlyList<QdrantPoint> Points);

    private sealed record QdrantPoint(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("vector")] float[] Vector,
        [property: JsonPropertyName("payload")] IReadOnlyDictionary<string, object?> Payload);

    private sealed record QdrantSearchRequest(
        [property: JsonPropertyName("vector")] float[] Vector,
        [property: JsonPropertyName("limit")] int Limit,
        [property: JsonPropertyName("with_payload")] bool WithPayload,
        [property: JsonPropertyName("filter")] QdrantFilter? Filter = null);

    private sealed record QdrantFilter([property: JsonPropertyName("must")] IReadOnlyList<QdrantFieldCondition> Must);

    private sealed record QdrantFieldCondition(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("match")] QdrantMatchValue Match);

    private sealed record QdrantMatchValue([property: JsonPropertyName("value")] string Value);

    private sealed record QdrantSearchResponse([property: JsonPropertyName("result")] IReadOnlyList<QdrantSearchPoint> Result);

    private sealed record QdrantSearchPoint(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("payload")] JsonElement Payload);

    private sealed record QdrantCollectionsResponse([property: JsonPropertyName("result")] QdrantCollectionsResult Result);

    private sealed record QdrantCollectionsResult([property: JsonPropertyName("collections")] IReadOnlyList<QdrantCollectionInfo> Collections);

    private sealed record QdrantCollectionInfo([property: JsonPropertyName("name")] string Name);

    private sealed record QdrantCountRequest(
        [property: JsonPropertyName("exact")] bool Exact,
        [property: JsonPropertyName("filter")] QdrantFilter? Filter = null);

    private sealed record QdrantCountResponse([property: JsonPropertyName("result")] QdrantCountResult Result);

    private sealed record QdrantCountResult([property: JsonPropertyName("count")] int Count);
}

file static class QdrantPayloadExtensions
{
    public static string? TryGetString(this JsonElement payload, string propertyName) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    public static IReadOnlyDictionary<string, string> TryGetMetadata(this JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("metadata", out var metadata)
            || metadata.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        return metadata.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText());
    }
}
