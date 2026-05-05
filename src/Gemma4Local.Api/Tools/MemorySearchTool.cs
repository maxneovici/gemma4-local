using System.Text.Json;
using Gemma4Local.Api.Memory;

namespace Gemma4Local.Api.Tools;

public sealed class MemorySearchTool(ILocalMemoryStore memoryStore) : ILocalTool
{
    public string Name => "memory_search";

    public string Description => "Search local vector memory for relevant context.";

    public string ArgumentsJsonSchema => "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"collection\":{\"type\":\"string\"},\"limit\":{\"type\":\"integer\"}},\"required\":[\"query\"]}";

    public async Task<LocalToolResult> InvokeAsync(LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var query = GetRequiredString(invocation.Arguments, "query");
        var collection = GetString(invocation.Arguments, "collection") ?? invocation.Agent.Name;
        var limit = GetInt(invocation.Arguments, "limit") ?? 5;
        var results = await memoryStore.SearchAsync(new MemorySearchRequest(collection, query, limit), cancellationToken);

        if (results.Count == 0)
        {
            return new LocalToolResult("No matching local memories found.");
        }

        return new LocalToolResult(string.Join(Environment.NewLine, results.Select(result => $"[{result.Score:0.000}] {result.Text}")));
    }

    private static string GetRequiredString(IReadOnlyDictionary<string, JsonElement> arguments, string name) =>
        GetString(arguments, name) ?? throw new ArgumentException($"Tool argument '{name}' is required.");

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> arguments, string name) =>
        arguments.TryGetValue(name, out var value) ? value.GetString() : null;

    private static int? GetInt(IReadOnlyDictionary<string, JsonElement> arguments, string name) =>
        arguments.TryGetValue(name, out var value) && value.TryGetInt32(out var number) ? number : null;
}
