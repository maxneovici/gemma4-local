using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Agents;
using LLLMax.Api.Memory;

namespace LLLMax.Api.Tools;

public sealed class MemorySearchTool(ILocalMemoryStore memoryStore, IMemoryRecallPlanner recallPlanner) : LocalToolBase<MemorySearchArguments>
{
    public override string Name => "memory_search";

    public override string Description => "Search local vector memory for relevant context.";

    protected override async Task<LocalToolResult> InvokeAsync(MemorySearchArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var results = await SearchDefaultMemoryBandsAsync(arguments, invocation, cancellationToken);

        if (results.Count == 0)
        {
            return new LocalToolResult("No matching local memories found.");
        }

        return new LocalToolResult(
            string.Join(Environment.NewLine + Environment.NewLine, results.Select(FormatResult)),
            results.Select(ToCitation).ToList());
    }

    private static string FormatResult(MemorySearchResult result)
    {
        var source = GetMetadata(result, "sourceFile") ?? GetMetadata(result, "source") ?? result.Id;
        var chunk = GetMetadata(result, "chunkIndex");
        var category = GetMetadata(result, "category");
        var collection = GetMetadata(result, "collection");
        var kind = GetMetadata(result, "kind");
        var topic = GetMetadata(result, "topic");
        var tenant = GetMetadata(result, "tenant");
        var metadata = string.Join(", ", new[]
        {
            collection is null ? null : $"collection={collection}",
            $"source={source}",
            kind is null ? null : $"kind={kind}",
            chunk is null ? null : $"chunk={chunk}",
            category is null ? null : $"category={category}",
            topic is null ? null : $"topic={topic}",
            tenant is null ? null : $"tenant={tenant}"
        }.Where(item => item is not null));

        return $"[{result.Score:0.000}] {metadata}\n{result.Text}";
    }

    private async Task<IReadOnlyList<MemorySearchResult>> SearchDefaultMemoryBandsAsync(MemorySearchArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(arguments.Limit ?? 12, 8, 24);
        var bands = new List<(MemorySearchResult Result, int Priority, int QueryIndex)>();
        var collections = GetDefaultCollections();
        var plan = await recallPlanner.PlanAsync(arguments.Query, invocation.Messages, cancellationToken);
        var queries = MemoryRecallPolicy.BuildQueries(arguments.Query, invocation.Messages, plan);

        for (var queryIndex = 0; queryIndex < queries.Count; queryIndex++)
        {
            var query = queries[queryIndex];

            foreach (var collection in collections)
            {
                await AddBandAsync(bands, collection.Name, query, limit, collection.Priority, queryIndex, MergeFilter(arguments.Filter, collection.Filter ?? new Dictionary<string, string>()), cancellationToken);
            }
        }

        await ExpandRelatedMemoriesAsync(bands, cancellationToken);

        var candidates = MemoryRecallPolicy.SelectTopDiverse(bands, queries, plan, Math.Max(limit * 4, limit));
        var ids = await recallPlanner.RerankAsync(arguments.Query, plan, candidates, limit, cancellationToken);
        return OrderByIds(candidates, ids, limit);
    }

    private static IReadOnlyList<MemoryCollectionBand> GetDefaultCollections() =>
    [
        new(MemoryLayers.Memory, 0, null)
    ];

    private async Task AddBandAsync(
        ICollection<(MemorySearchResult Result, int Priority, int QueryIndex)> bands,
        string collection,
        string query,
        int limit,
        int priority,
        int queryIndex,
        IReadOnlyDictionary<string, string>? filter,
        CancellationToken cancellationToken)
    {
        var results = await memoryStore.SearchAsync(new MemorySearchRequest(collection, query, limit, filter), cancellationToken);

        foreach (var result in results)
        {
            bands.Add((WithCollectionMetadata(result, collection), priority, queryIndex));
        }
    }

    private static IReadOnlyDictionary<string, string>? MergeFilter(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string> right)
    {
        if (left is null || left.Count == 0)
        {
            return right;
        }

        var merged = left.ToDictionary(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in right)
        {
            merged.TryAdd(pair.Key, pair.Value);
        }

        return merged;
    }

    private static MemorySearchResult WithCollectionMetadata(MemorySearchResult result, string collection)
    {
        var metadata = result.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
        metadata.TryAdd("collection", collection);
        return result with { Metadata = metadata };
    }

    private async Task ExpandRelatedMemoriesAsync(ICollection<(MemorySearchResult Result, int Priority, int QueryIndex)> bands, CancellationToken cancellationToken)
    {
        var seedSessions = bands
            .Select(item => new { SessionId = GetMetadata(item.Result, "sessionId"), item.Result.Score, item.Priority, item.QueryIndex })
            .Where(item => !string.IsNullOrWhiteSpace(item.SessionId))
            .GroupBy(item => item.SessionId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Priority).ThenByDescending(item => item.Score).First())
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Score)
            .Take(2)
            .ToList();

        foreach (var seed in seedSessions)
        {
            var related = await memoryStore.InspectCollectionAsync(MemoryLayers.Memory, new MemoryCollectionInspectRequest(
                Limit: 16,
                Filter: new Dictionary<string, string> { ["sessionId"] = seed.SessionId! }), cancellationToken);

            foreach (var record in related.Records)
            {
                if (MemoryRecallPolicy.IsNoiseResult(new MemorySearchResult(record.Id, record.TextPreview, seed.Score, record.Metadata)))
                {
                    continue;
                }

                var metadata = record.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
                metadata.TryAdd("collection", MemoryLayers.Memory);
                bands.Add((new MemorySearchResult(record.Id, record.TextPreview, Math.Min(seed.Score, 0.55), metadata), seed.Priority + 3, seed.QueryIndex));
            }
        }
    }

    private sealed record MemoryCollectionBand(string Name, int Priority, IReadOnlyDictionary<string, string>? Filter);

    private static IReadOnlyList<MemorySearchResult> OrderByIds(IReadOnlyList<MemorySearchResult> candidates, IReadOnlyList<string> ids, int limit)
    {
        var selected = ids
            .Select(id => candidates.FirstOrDefault(candidate => candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (!selected.Any(item => item.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase)))
            {
                selected.Add(candidate);
            }
        }

        return selected.Take(limit).ToList();
    }

    private static string? GetMetadata(MemorySearchResult result, string key) =>
        result.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static CitationSource ToCitation(MemorySearchResult result)
    {
        var source = GetMetadata(result, "sourceFile") ?? GetMetadata(result, "source");
        var chunk = GetMetadata(result, "chunkIndex");
        var kind = GetMetadata(result, "kind") ?? "memory";
        var title = source is null
            ? "Local memory"
            : chunk is null ? source : $"{source} chunk {chunk}";

        return new CitationSource(
            Kind: kind.Equals("document_chunk", StringComparison.OrdinalIgnoreCase) ? "document" : "memory",
            Title: title,
            Source: source,
            Chunk: chunk,
            Score: result.Score);
    }
}

public sealed record MemorySearchArguments(
    [property: Required] string Query,
    string? Collection = null,
    int? Limit = null,
    IReadOnlyDictionary<string, string>? Filter = null);
