using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Agents;
using LLLMax.Api.Memory;

namespace LLLMax.Api.Tools;

public sealed class KnowledgeSearchTool(ILocalMemoryStore memoryStore) : LocalToolBase<KnowledgeSearchArguments>
{
    public override string Name => "knowledge_search";

    public override string Description => "Search external/local knowledge such as documents, manuals, OCR text, invoices, and reference material. Does not search personal user memory.";

    protected override async Task<LocalToolResult> InvokeAsync(KnowledgeSearchArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var filter = MemoryLayers.WithLayer(arguments.Filter, MemoryLayers.Knowledge);
        var results = await memoryStore.SearchAsync(new MemorySearchRequest(
            Collection: string.IsNullOrWhiteSpace(arguments.Collection) ? MemoryLayers.Knowledge : arguments.Collection.Trim(),
            Query: arguments.Query,
            Limit: Math.Clamp(arguments.Limit ?? 8, 1, 24),
            Filter: filter), cancellationToken);
        results = results.Where(result => !MemoryMetadata.IsSuppressedForRecall(result.Metadata)).ToList();

        if (results.Count == 0)
        {
            return new LocalToolResult("No matching local knowledge found.");
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
        var collection = GetMetadata(result, "collection") ?? MemoryLayers.Knowledge;
        var metadata = string.Join(", ", new[]
        {
            $"collection={collection}",
            $"source={source}",
            chunk is null ? null : $"chunk={chunk}",
            category is null ? null : $"category={category}",
            tenant is null ? null : $"tenant={tenant}"
        }.Where(item => item is not null));

        return $"[{result.Score:0.000}] {metadata}\n{result.Text}";
    }

    private static CitationSource ToCitation(MemorySearchResult result)
    {
        var source = GetMetadata(result, "sourceFile") ?? GetMetadata(result, "source");
        var chunk = GetMetadata(result, "chunkIndex");

        return new CitationSource(
            Kind: "knowledge",
            Title: source is null ? "Local knowledge" : chunk is null ? source : $"{source} chunk {chunk}",
            Source: source,
            Chunk: chunk,
            Score: result.Score);
    }

    private static string? GetMetadata(MemorySearchResult result, string key) =>
        result.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public sealed record KnowledgeSearchArguments(
    [property: Required] string Query,
    string? Collection = null,
    int? Limit = null,
    IReadOnlyDictionary<string, string>? Filter = null);
