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
        var metadata = (arguments.Metadata ?? new Dictionary<string, string>()).ToDictionary(StringComparer.OrdinalIgnoreCase);
        metadata.TryAdd("agent", invocation.Agent.Name);
        metadata.TryAdd("conversationId", invocation.ConversationId ?? string.Empty);
        metadata.TryAdd("kind", "model_memory");
        metadata.TryAdd("observedAt", DateTimeOffset.UtcNow.ToString("O"));

        if (collection.Equals("profile", StringComparison.OrdinalIgnoreCase) || metadata.ContainsKey("category") || LooksLikeProfileMemory(arguments.Text))
        {
            metadata.TryAdd("category", "profile");
            metadata.TryAdd("subject", "user");
        }

        var response = await memoryStore.UpsertAsync(new MemoryUpsertRequest(
            Collection: collection,
            Text: arguments.Text,
            Metadata: metadata), cancellationToken);

        return new LocalToolResult($"Stored memory {response.Id} in collection {response.Collection}.");
    }

    private static bool LooksLikeProfileMemory(string text)
    {
        var lower = text.ToLowerInvariant();
        return ContainsAny(lower, "user ", "user's", "max ", "prefers", "likes", "loves", "usually", "main ", "favorite", "favourite", "plays", "works", "lives", "family", "goal", "wants");
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}

public sealed record MemoryWriteArguments(
    [property: Required] string Text,
    string? Collection = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
