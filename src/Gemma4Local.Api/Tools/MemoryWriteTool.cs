using System.Text.Json;
using Gemma4Local.Api.Memory;

namespace Gemma4Local.Api.Tools;

public sealed class MemoryWriteTool(ILocalMemoryStore memoryStore) : ILocalTool
{
    public string Name => "memory_write";

    public string Description => "Store useful text in local vector memory.";

    public string ArgumentsJsonSchema => "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"},\"collection\":{\"type\":\"string\"}},\"required\":[\"text\"]}";

    public async Task<LocalToolResult> InvokeAsync(LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var text = GetRequiredString(invocation.Arguments, "text");
        var collection = GetString(invocation.Arguments, "collection") ?? invocation.Agent.Name;

        var response = await memoryStore.UpsertAsync(new MemoryUpsertRequest(
            Collection: collection,
            Text: text,
            Metadata: new Dictionary<string, string>
            {
                ["agent"] = invocation.Agent.Name,
                ["conversationId"] = invocation.ConversationId ?? string.Empty
            }), cancellationToken);

        return new LocalToolResult($"Stored memory {response.Id} in collection {response.Collection}.");
    }

    private static string GetRequiredString(IReadOnlyDictionary<string, JsonElement> arguments, string name) =>
        GetString(arguments, name) ?? throw new ArgumentException($"Tool argument '{name}' is required.");

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> arguments, string name) =>
        arguments.TryGetValue(name, out var value) ? value.GetString() : null;
}
