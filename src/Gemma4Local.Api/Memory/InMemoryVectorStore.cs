using System.Collections.Concurrent;

namespace Gemma4Local.Api.Memory;

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
            .Select(record => new MemorySearchResult(
                Id: record.Id,
                Text: record.Text,
                Score: CosineSimilarity(queryVector, record.Vector),
                Metadata: record.Metadata))
            .OrderByDescending(result => result.Score)
            .Take(request.Limit)
            .ToList();
    }

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
}
