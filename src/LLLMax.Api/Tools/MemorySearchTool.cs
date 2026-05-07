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
        var collection = arguments.Collection ?? invocation.Agent.Name;
        var results = await memoryStore.SearchAsync(new MemorySearchRequest(collection, arguments.Query, arguments.Limit ?? 5, arguments.Filter), cancellationToken);

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
        var tenant = GetMetadata(result, "tenant");
        var metadata = string.Join(", ", new[]
        {
            $"source={source}",
            chunk is null ? null : $"chunk={chunk}",
            category is null ? null : $"category={category}",
            tenant is null ? null : $"tenant={tenant}"
        }.Where(item => item is not null));

        return $"[{result.Score:0.000}] {metadata}\n{result.Text}";
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
