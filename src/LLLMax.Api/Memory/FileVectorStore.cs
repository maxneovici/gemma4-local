using System.Collections.Concurrent;
using System.Text.Json;
using LLLMax.Api.Services;

namespace LLLMax.Api.Memory;

public sealed class FileVectorStore(IEmbeddingGenerator embeddingGenerator, LocalDataPaths paths) : ILocalMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

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
        var record = new MemoryRecord(
            Id: id,
            Collection: request.Collection,
            Text: request.Text,
            Vector: await embeddingGenerator.GenerateAsync(request.Text, cancellationToken),
            Metadata: request.Metadata ?? new Dictionary<string, string>());

        var gate = GetLock(request.Collection);
        await gate.WaitAsync(cancellationToken);

        try
        {
            var records = await ReadCollectionAsync(request.Collection, cancellationToken);
            records.Add(record);
            await WriteCollectionAsync(request.Collection, records, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return new MemoryUpsertResponse(id, request.Collection);
    }

    public async Task<MemoryBatchUpsertResponse> UpsertBatchAsync(MemoryBatchUpsertRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Collection))
        {
            throw new ArgumentException("Collection is required.", nameof(request));
        }

        var records = new List<MemoryRecord>();

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Text))
            {
                continue;
            }

            records.Add(new MemoryRecord(
                Id: Guid.NewGuid().ToString("n"),
                Collection: request.Collection,
                Text: item.Text,
                Vector: await embeddingGenerator.GenerateAsync(item.Text, cancellationToken),
                Metadata: item.Metadata ?? new Dictionary<string, string>()));
        }

        if (records.Count == 0)
        {
            return new MemoryBatchUpsertResponse([], request.Collection);
        }

        var gate = GetLock(request.Collection);
        await gate.WaitAsync(cancellationToken);

        try
        {
            var existing = await ReadCollectionAsync(request.Collection, cancellationToken);
            existing.AddRange(records);
            await WriteCollectionAsync(request.Collection, existing, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return new MemoryBatchUpsertResponse(records.Select(record => record.Id).ToList(), request.Collection);
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemorySearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Collection) || string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var gate = GetLock(request.Collection);
        await gate.WaitAsync(cancellationToken);
        List<MemoryRecord> records;

        try
        {
            records = await ReadCollectionAsync(request.Collection, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        if (records.Count == 0)
        {
            return [];
        }

        var queryVector = await embeddingGenerator.GenerateAsync(request.Query, cancellationToken);

        return records
            .Where(record => MatchesFilter(record.Metadata, request.Filter))
            .Select(record => new MemorySearchResult(
                Id: record.Id,
                Text: record.Text,
                Score: CosineSimilarity(queryVector, record.Vector),
                Metadata: record.Metadata))
            .OrderByDescending(result => result.Score)
            .Take(Math.Clamp(request.Limit, 1, 50))
            .ToList();
    }

    public async Task<MemoryCountResponse> CountAsync(MemoryCountRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Collection))
        {
            return new MemoryCountResponse(request.Collection, 0);
        }

        var gate = GetLock(request.Collection);
        await gate.WaitAsync(cancellationToken);
        List<MemoryRecord> records;

        try
        {
            records = await ReadCollectionAsync(request.Collection, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return new MemoryCountResponse(request.Collection, records.Count(record => MatchesFilter(record.Metadata, request.Filter)));
    }

    public async Task<MemoryStatsResponse> GetStatsAsync(CancellationToken cancellationToken)
    {
        var directory = paths.MemoryDirectory;
        var collections = new List<MemoryCollectionStats>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var records = await JsonSerializer.DeserializeAsync<List<MemoryRecord>>(stream, JsonOptions, cancellationToken) ?? [];
            collections.Add(new MemoryCollectionStats(Path.GetFileNameWithoutExtension(file), records.Count));
        }

        collections = collections.OrderBy(collection => collection.Name).ToList();

        return new MemoryStatsResponse(
            Provider: "File",
            CollectionCount: collections.Count,
            RecordCount: collections.Sum(collection => collection.RecordCount),
            Collections: collections);
    }

    private SemaphoreSlim GetLock(string collection) =>
        _locks.GetOrAdd(collection, _ => new SemaphoreSlim(1, 1));

    private async Task<List<MemoryRecord>> ReadCollectionAsync(string collection, CancellationToken cancellationToken)
    {
        var file = GetCollectionPath(collection);

        if (!File.Exists(file))
        {
            return [];
        }

        await using var stream = File.OpenRead(file);

        return await JsonSerializer.DeserializeAsync<List<MemoryRecord>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteCollectionAsync(string collection, List<MemoryRecord> records, CancellationToken cancellationToken)
    {
        var file = GetCollectionPath(collection);
        await using var stream = File.Create(file);
        await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
    }

    private string GetCollectionPath(string collection)
    {
        var safeName = string.Join("_", collection.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(paths.MemoryDirectory, $"{safeName}.json");
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

        return leftMagnitude == 0 || rightMagnitude == 0
            ? 0
            : dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static bool MatchesFilter(IReadOnlyDictionary<string, string> metadata, IReadOnlyDictionary<string, string>? filter) =>
        filter is null
        || filter.Count == 0
        || filter.All(pair => metadata.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
}
