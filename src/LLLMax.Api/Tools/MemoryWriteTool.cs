using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Memory;

namespace LLLMax.Api.Tools;

public sealed class MemoryWriteTool(ILocalMemoryStore memoryStore) : LocalToolBase<MemoryWriteArguments>
{
    public override string Name => "memory_write";

    public override string Description => "Store useful text in local vector memory.";

    protected override async Task<LocalToolResult> InvokeAsync(MemoryWriteArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var metadata = MemoryLayers.WithLayer(arguments.Metadata, MemoryLayers.Memory);
        metadata.TryAdd("agent", invocation.Agent.Name);
        metadata.TryAdd("conversationId", invocation.ConversationId ?? string.Empty);
        metadata.TryAdd("kind", "model_memory");
        metadata.TryAdd("observedAt", DateTimeOffset.UtcNow.ToString("O"));
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
    IReadOnlyDictionary<string, string>? Metadata = null);
