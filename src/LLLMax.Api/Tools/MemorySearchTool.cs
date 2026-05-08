using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Agents;
using LLLMax.Api.Memory;

namespace LLLMax.Api.Tools;

public sealed class MemorySearchTool(ILocalMemoryStore memoryStore) : LocalToolBase<MemorySearchArguments>
{
    public override string Name => "memory_search";

    public override string Description => "Search local vector memory for relevant context.";

    protected override async Task<LocalToolResult> InvokeAsync(MemorySearchArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var results = string.IsNullOrWhiteSpace(arguments.Collection)
            ? await SearchDefaultMemoryBandsAsync(arguments, invocation, cancellationToken)
            : await memoryStore.SearchAsync(new MemorySearchRequest(arguments.Collection, arguments.Query, arguments.Limit ?? 5, arguments.Filter), cancellationToken);

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
        var limit = Math.Clamp(arguments.Limit ?? 8, 6, 20);
        var bands = new List<(MemorySearchResult Result, int Priority)>();

        await AddBandAsync(bands, "profile_canonical", arguments.Query, limit, -1, MergeFilter(arguments.Filter, new Dictionary<string, string> { ["kind"] = "canonical_profile_fact" }), cancellationToken);
        await AddBandAsync(bands, "core", arguments.Query, limit, 0, MergeFilter(arguments.Filter, new Dictionary<string, string> { ["category"] = "profile" }), cancellationToken);
        await AddBandAsync(bands, "profile", arguments.Query, limit, 1, arguments.Filter, cancellationToken);
        await AddBandAsync(bands, invocation.Agent.Name, arguments.Query, limit, 2, arguments.Filter, cancellationToken);

        if (!invocation.Agent.Name.Equals("core", StringComparison.OrdinalIgnoreCase))
        {
            await AddBandAsync(bands, "core", arguments.Query, Math.Max(1, limit / 2), 3, MergeFilter(arguments.Filter, new Dictionary<string, string> { ["category"] = "session_summary" }), cancellationToken);
        }

        await ExpandRelatedCoreProfileMemoriesAsync(bands, cancellationToken);

        return bands
            .GroupBy(item => item.Result.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(RankDefaultMemoryResult).First())
            .OrderByDescending(RankDefaultMemoryResult)
            .Select(item => item.Result)
            .Take(limit)
            .ToList();
    }

    private static double RankDefaultMemoryResult((MemorySearchResult Result, int Priority) item) =>
        item.Result.Score + (item.Priority < 0 ? 0.05 : 0) - (Math.Max(item.Priority, 0) * 0.05);

    private async Task AddBandAsync(
        ICollection<(MemorySearchResult Result, int Priority)> bands,
        string collection,
        string query,
        int limit,
        int priority,
        IReadOnlyDictionary<string, string>? filter,
        CancellationToken cancellationToken)
    {
        var results = await memoryStore.SearchAsync(new MemorySearchRequest(collection, query, limit, filter), cancellationToken);

        foreach (var result in results)
        {
            bands.Add((WithCollectionMetadata(result, collection), priority));
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

    private async Task ExpandRelatedCoreProfileMemoriesAsync(ICollection<(MemorySearchResult Result, int Priority)> bands, CancellationToken cancellationToken)
    {
        var seedSessions = bands
            .Where(item => GetMetadata(item.Result, "collection")?.Equals("core", StringComparison.OrdinalIgnoreCase) == true)
            .Where(item => GetMetadata(item.Result, "category")?.Equals("profile", StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => new { SessionId = GetMetadata(item.Result, "sessionId"), item.Result.Score, item.Priority })
            .Where(item => !string.IsNullOrWhiteSpace(item.SessionId))
            .GroupBy(item => item.SessionId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Priority).ThenByDescending(item => item.Score).First())
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Score)
            .Take(2)
            .ToList();

        foreach (var seed in seedSessions)
        {
            var related = await memoryStore.InspectCollectionAsync("core", new MemoryCollectionInspectRequest(
                Limit: 12,
                Filter: new Dictionary<string, string>
                {
                    ["sessionId"] = seed.SessionId!,
                    ["category"] = "profile"
                }), cancellationToken);

            foreach (var record in related.Records)
            {
                var metadata = record.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
                metadata.TryAdd("collection", "core");
                bands.Add((new MemorySearchResult(record.Id, record.TextPreview, Math.Max(0, seed.Score - 0.01), metadata), seed.Priority));
            }
        }
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
