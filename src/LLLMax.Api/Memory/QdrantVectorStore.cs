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

    public async Task<IReadOnlyList<MemoryCollectionDetail>> ListCollectionsAsync(CancellationToken cancellationToken)
    {
        var response = await Client.GetAsync("/collections", cancellationToken);
        await ThrowIfFailedAsync(response, "Qdrant collection list", cancellationToken);
        var collections = await response.Content.ReadFromJsonAsync<QdrantCollectionsResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Qdrant returned an empty collections response.");
        var details = new List<MemoryCollectionDetail>();

        foreach (var collection in collections.Result.Collections.OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase))
        {
            var detail = await GetCollectionAsync(collection.Name, cancellationToken);

            if (detail is not null)
            {
                details.Add(detail);
            }
        }

        return details;
    }

    public async Task<MemoryCollectionDetail?> GetCollectionAsync(string collection, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCollection(collection);
        var response = await Client.GetAsync($"/collections/{Uri.EscapeDataString(normalized)}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfFailedAsync(response, $"Qdrant collection lookup for {normalized}", cancellationToken);
        var detail = await response.Content.ReadFromJsonAsync<QdrantCollectionDetailResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Qdrant returned an empty collection detail response.");
        var vectorConfig = detail.Result.Config.Params.Vectors;

        return new MemoryCollectionDetail(
            Name: normalized,
            RecordCount: detail.Result.PointsCount,
            Provider: "Qdrant",
            Status: detail.Result.Status,
            VectorSize: vectorConfig.Size,
            Distance: vectorConfig.Distance);
    }

    public async Task<MemoryCollectionInspectResponse> InspectCollectionAsync(string collection, MemoryCollectionInspectRequest request, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCollection(collection);

        if (!await CollectionExistsAsync(normalized, cancellationToken))
        {
            return new MemoryCollectionInspectResponse(normalized, 0, null, []);
        }

        var limit = Math.Clamp(request.Limit, 1, 100);
        var filter = BuildFilter(request.Filter);
        var countResponse = await CountAsync(new MemoryCountRequest(normalized, request.Filter), cancellationToken);
        var response = await Client.PostAsJsonAsync($"/collections/{Uri.EscapeDataString(normalized)}/points/scroll", new QdrantScrollRequest(
            Limit: limit,
            WithPayload: true,
            WithVector: false,
            Offset: request.Cursor,
            Filter: filter), JsonOptions, cancellationToken);

        await ThrowIfFailedAsync(response, $"Qdrant scroll for {normalized}", cancellationToken);
        var scroll = await response.Content.ReadFromJsonAsync<QdrantScrollResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Qdrant returned an empty scroll response.");
        var records = scroll.Result.Points
            .Select(point =>
            {
                var text = point.Payload.TryGetString("text") ?? string.Empty;
                return new MemoryCollectionRecordPreview(point.Id, CreatePreview(text), text.Length, point.Payload.TryGetMetadata());
            })
            .ToList();

        return new MemoryCollectionInspectResponse(normalized, countResponse.Count, scroll.Result.NextPageOffset?.ToString(), records);
    }

    public async Task<MemoryRecordDetail?> GetRecordAsync(string collection, string id, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCollection(collection);

        if (string.IsNullOrWhiteSpace(id) || !await CollectionExistsAsync(normalized, cancellationToken))
        {
            return null;
        }

        var response = await Client.PostAsJsonAsync($"/collections/{Uri.EscapeDataString(normalized)}/points", new QdrantPointLookupRequest(
            Ids: [id],
            WithPayload: true,
            WithVector: false), JsonOptions, cancellationToken);
        await ThrowIfFailedAsync(response, $"Qdrant point lookup for {normalized}/{id}", cancellationToken);
        var lookup = await response.Content.ReadFromJsonAsync<QdrantPointLookupResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Qdrant returned an empty point lookup response.");
        var point = lookup.Result.FirstOrDefault();

        return point is null ? null : ToRecordDetail(normalized, point.Id, point.Payload);
    }

    public async Task<MemoryRecordDetail?> UpdateRecordAsync(string collection, string id, MemoryRecordUpdateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new InvalidOperationException("Memory text is required.");
        }

        var normalized = NormalizeCollection(collection);
        var existing = await GetRecordAsync(normalized, id, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        var vector = await embeddingGenerator.GenerateAsync(request.Text, cancellationToken);
        await EnsureCollectionAsync(normalized, vector.Length, cancellationToken);
        var response = await Client.PutAsJsonAsync($"/collections/{Uri.EscapeDataString(normalized)}/points?wait=true", new QdrantUpsertRequest(
        [
            new QdrantPoint(
                Id: id,
                Vector: vector,
                Payload: new Dictionary<string, object?>
                {
                    ["collection"] = normalized,
                    ["text"] = request.Text,
                    ["metadata"] = request.Metadata ?? new Dictionary<string, string>()
                })
        ]), JsonOptions, cancellationToken);
        await ThrowIfFailedAsync(response, $"Qdrant point update for {normalized}/{id}", cancellationToken);

        return new MemoryRecordDetail(id, normalized, request.Text, request.Text.Length, request.Metadata ?? new Dictionary<string, string>());
    }

    public async Task<MemoryRecordDeleteResponse> DeleteRecordAsync(string collection, string id, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCollection(collection);

        if (string.IsNullOrWhiteSpace(id) || !await CollectionExistsAsync(normalized, cancellationToken))
        {
            return new MemoryRecordDeleteResponse(normalized, id, false);
        }

        var existing = await GetRecordAsync(normalized, id, cancellationToken);

        if (existing is null)
        {
            return new MemoryRecordDeleteResponse(normalized, id, false);
        }

        var response = await Client.PostAsJsonAsync($"/collections/{Uri.EscapeDataString(normalized)}/points/delete?wait=true", new QdrantPointDeleteRequest(
            Points: [id]), JsonOptions, cancellationToken);
        await ThrowIfFailedAsync(response, $"Qdrant point delete for {normalized}/{id}", cancellationToken);
        return new MemoryRecordDeleteResponse(normalized, id, true);
    }

    public async Task<MemoryCollectionDeleteResponse> DeleteCollectionAsync(string collection, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCollection(collection);
        var response = await Client.DeleteAsync($"/collections/{Uri.EscapeDataString(normalized)}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new MemoryCollectionDeleteResponse(normalized, false);
        }

        await ThrowIfFailedAsync(response, $"Qdrant delete collection {normalized}", cancellationToken);
        return new MemoryCollectionDeleteResponse(normalized, true);
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

    private static MemoryRecordDetail ToRecordDetail(string collection, string id, JsonElement payload)
    {
        var text = payload.TryGetString("text") ?? string.Empty;
        return new MemoryRecordDetail(id, collection, text, text.Length, payload.TryGetMetadata());
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

    private sealed record QdrantCollectionDetailResponse([property: JsonPropertyName("result")] QdrantCollectionDetail Result);

    private sealed record QdrantCollectionDetail(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("points_count")] int PointsCount,
        [property: JsonPropertyName("config")] QdrantCollectionConfig Config);

    private sealed record QdrantCollectionConfig([property: JsonPropertyName("params")] QdrantCollectionParams Params);

    private sealed record QdrantCollectionParams([property: JsonPropertyName("vectors")] QdrantVectorParams Vectors);

    private sealed record QdrantCountRequest(
        [property: JsonPropertyName("exact")] bool Exact,
        [property: JsonPropertyName("filter")] QdrantFilter? Filter = null);

    private sealed record QdrantCountResponse([property: JsonPropertyName("result")] QdrantCountResult Result);

    private sealed record QdrantCountResult([property: JsonPropertyName("count")] int Count);

    private sealed record QdrantScrollRequest(
        [property: JsonPropertyName("limit")] int Limit,
        [property: JsonPropertyName("with_payload")] bool WithPayload,
        [property: JsonPropertyName("with_vector")] bool WithVector,
        [property: JsonPropertyName("offset")] string? Offset = null,
        [property: JsonPropertyName("filter")] QdrantFilter? Filter = null);

    private sealed record QdrantScrollResponse([property: JsonPropertyName("result")] QdrantScrollResult Result);

    private sealed record QdrantScrollResult(
        [property: JsonPropertyName("points")] IReadOnlyList<QdrantScrollPoint> Points,
        [property: JsonPropertyName("next_page_offset")] JsonElement? NextPageOffset);

    private sealed record QdrantScrollPoint(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("payload")] JsonElement Payload);

    private sealed record QdrantPointLookupRequest(
        [property: JsonPropertyName("ids")] IReadOnlyList<string> Ids,
        [property: JsonPropertyName("with_payload")] bool WithPayload,
        [property: JsonPropertyName("with_vector")] bool WithVector);

    private sealed record QdrantPointLookupResponse([property: JsonPropertyName("result")] IReadOnlyList<QdrantScrollPoint> Result);

    private sealed record QdrantPointDeleteRequest([property: JsonPropertyName("points")] IReadOnlyList<string> Points);

    private static string CreatePreview(string text)
    {
        var compact = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 420 ? compact : compact[..420] + "...";
    }
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
