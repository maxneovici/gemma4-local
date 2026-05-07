using System.ComponentModel.DataAnnotations;
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

        return new LocalToolResult(string.Join(Environment.NewLine, results.Select(result => $"[{result.Score:0.000}] {result.Text}")));
    }
}

public sealed record MemorySearchArguments(
    [property: Required] string Query,
    string? Collection = null,
    int? Limit = null,
    IReadOnlyDictionary<string, string>? Filter = null);
