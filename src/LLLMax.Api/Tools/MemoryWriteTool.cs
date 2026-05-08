using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Memory;

namespace LLLMax.Api.Tools;

public sealed class MemoryWriteTool(ILocalMemoryStore memoryStore) : LocalToolBase<MemoryWriteArguments>
{
    public override string Name => "memory_write";

    public override string Description => "Store useful text in local vector memory.";

    protected override async Task<LocalToolResult> InvokeAsync(MemoryWriteArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var metadata = MemoryMetadata.Build(
            arguments.Metadata,
            MemoryLayers.Memory,
            arguments.Text,
            provenance: "tool_call",
            memoryType: arguments.MemoryType,
            conversationId: invocation.ConversationId,
            sourceMessageRole: "user",
            confidence: 0.82,
            reviewRequired: false);
        metadata.TryAdd("agent", invocation.Agent.Name);
        metadata.TryAdd("kind", "model_memory");
        metadata.TryAdd("category", "profile");
        metadata.TryAdd("subject", "user");

        var response = await memoryStore.UpsertAsync(new MemoryUpsertRequest(
            Collection: MemoryLayers.Memory,
            Text: arguments.Text,
            Metadata: metadata), cancellationToken);

        return new LocalToolResult($"Stored memory {response.Id} in collection {response.Collection}.");
    }

}

public sealed record MemoryWriteArguments(
    [property: Required] string Text,
    string? Collection = null,
    string? MemoryType = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
