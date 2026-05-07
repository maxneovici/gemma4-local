using System.Collections.Concurrent;

namespace LLLMax.Api.Memory;

public sealed class InMemoryVectorStore(IEmbeddingGenerator embeddingGenerator) : ILocalMemoryStore
{
    private readonly ConcurrentDictionary<string, List<MemoryRecord>> _collections = new(StringComparer.OrdinalIgnoreCase);

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

        var id = Guid.NewGuid().ToString("n");
        var vector = await embeddingGenerator.GenerateAsync(request.Text, cancellationToken);
        var record = new MemoryRecord(
            Id: id,
            Collection: request.Collection,
            Text: request.Text,
            Vector: vector,
            Metadata: request.Metadata ?? new Dictionary<string, string>());

        var collection = _collections.GetOrAdd(request.Collection, _ => []);

        lock (collection)
        {
            collection.Add(record);
        }

        return new MemoryUpsertResponse(id, request.Collection);
    }

    public async Task<MemoryBatchUpsertResponse> UpsertBatchAsync(MemoryBatchUpsertRequest request, CancellationToken cancellationToken)
    {
        var ids = new List<string>();

        foreach (var item in request.Items)
        {
            var response = await UpsertAsync(new MemoryUpsertRequest(request.Collection, item.Text, item.Metadata), cancellationToken);
            ids.Add(response.Id);
        }

        return new MemoryBatchUpsertResponse(ids, request.Collection);
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemorySearchRequest request, CancellationToken cancellationToken)
    {
        if (!_collections.TryGetValue(request.Collection, out var collection))
        {
            return [];
        }

        var queryVector = await embeddingGenerator.GenerateAsync(request.Query, cancellationToken);
        MemoryRecord[] snapshot;

        lock (collection)
        {
            snapshot = collection.ToArray();
        }

        return snapshot
            .Where(record => MatchesFilter(record.Metadata, request.Filter))
            .Select(record => new MemorySearchResult(
                Id: record.Id,
                Text: record.Text,
                Score: CosineSimilarity(queryVector, record.Vector),
                Metadata: record.Metadata))
            .OrderByDescending(result => result.Score)
            .Take(request.Limit)
            .ToList();
    }

    public Task<MemoryCountResponse> CountAsync(MemoryCountRequest request, CancellationToken cancellationToken)
    {
        if (!_collections.TryGetValue(request.Collection, out var collection))
        {
            return Task.FromResult(new MemoryCountResponse(request.Collection, 0));
        }

        MemoryRecord[] snapshot;

        lock (collection)
        {
            snapshot = collection.ToArray();
        }

        return Task.FromResult(new MemoryCountResponse(request.Collection, snapshot.Count(record => MatchesFilter(record.Metadata, request.Filter))));
    }

    public Task<MemoryStatsResponse> GetStatsAsync(CancellationToken cancellationToken)
    {
        var collections = _collections
            .Select(pair => new MemoryCollectionStats(pair.Key, pair.Value.Count))
            .OrderBy(collection => collection.Name)
            .ToList();

        return Task.FromResult(new MemoryStatsResponse(
            Provider: "InMemory",
            CollectionCount: collections.Count,
            RecordCount: collections.Sum(collection => collection.RecordCount),
            Collections: collections));
    }

    public Task<IReadOnlyList<MemoryCollectionDetail>> ListCollectionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MemoryCollectionDetail> details = _collections
            .Select(pair => new MemoryCollectionDetail(pair.Key, pair.Value.Count, "InMemory"))
            .OrderBy(collection => collection.Name)
            .ToList();

        return Task.FromResult(details);
    }

    public Task<MemoryCollectionDetail?> GetCollectionAsync(string collection, CancellationToken cancellationToken)
    {
        return Task.FromResult(_collections.TryGetValue(collection, out var records)
            ? new MemoryCollectionDetail(collection, records.Count, "InMemory")
            : null);
    }

    public Task<MemoryCollectionInspectResponse> InspectCollectionAsync(string collection, MemoryCollectionInspectRequest request, CancellationToken cancellationToken)
    {
        if (!_collections.TryGetValue(collection, out var records))
        {
            return Task.FromResult(new MemoryCollectionInspectResponse(collection, 0, null, []));
        }

        MemoryRecord[] snapshot;

        lock (records)
        {
            snapshot = records.ToArray();
        }

        var offset = int.TryParse(request.Cursor, out var parsedCursor) ? Math.Max(0, parsedCursor) : 0;
        var limit = Math.Clamp(request.Limit, 1, 100);
        var filtered = snapshot
            .Where(record => MatchesFilter(record.Metadata, request.Filter))
            .ToList();
        var page = filtered
            .Skip(offset)
            .Take(limit)
            .Select(record => new MemoryCollectionRecordPreview(record.Id, CreatePreview(record.Text), record.Text.Length, record.Metadata))
            .ToList();
        var nextOffset = offset + page.Count;

        return Task.FromResult(new MemoryCollectionInspectResponse(
            collection,
            filtered.Count,
            nextOffset < filtered.Count ? nextOffset.ToString() : null,
            page));
    }

    public Task<MemoryCollectionDeleteResponse> DeleteCollectionAsync(string collection, CancellationToken cancellationToken) =>
        Task.FromResult(new MemoryCollectionDeleteResponse(collection, _collections.TryRemove(collection, out _)));

    private static double CosineSimilarity(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;

        for (var i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static bool MatchesFilter(IReadOnlyDictionary<string, string> metadata, IReadOnlyDictionary<string, string>? filter) =>
        filter is null
        || filter.Count == 0
        || filter.All(pair => metadata.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value, StringComparison.OrdinalIgnoreCase));

    private static string CreatePreview(string text)
    {
        var compact = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 420 ? compact : compact[..420] + "...";
    }
}
