using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Memory;

namespace LLLMax.Api.Tools;

public sealed class MemoryWriteTool(ILocalMemoryStore memoryStore) : LocalToolBase<MemoryWriteArguments>
{
    public override string Name => "memory_write";

    public override string Description => "Store useful text in local vector memory.";

    protected override async Task<LocalToolResult> InvokeAsync(MemoryWriteArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var collection = arguments.Collection ?? invocation.Agent.Name;

        var response = await memoryStore.UpsertAsync(new MemoryUpsertRequest(
            Collection: collection,
            Text: arguments.Text,
            Metadata: new Dictionary<string, string>
            {
                ["agent"] = invocation.Agent.Name,
                ["conversationId"] = invocation.ConversationId ?? string.Empty
            }), cancellationToken);

        return new LocalToolResult($"Stored memory {response.Id} in collection {response.Collection}.");
    }
}

public sealed record MemoryWriteArguments([property: Required] string Text, string? Collection = null);
