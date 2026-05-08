using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Memory;

namespace LLLMax.Api.Tools;

public sealed class MemoryCorrectTool(ILocalMemoryStore memoryStore) : LocalToolBase<MemoryCorrectArguments>
{
    public override string Name => "memory_correct";

    public override string Description => "Correct local memory by superseding or forgetting an existing memory record. Use memory_search first to find the record id.";

    protected override async Task<LocalToolResult> InvokeAsync(MemoryCorrectArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var collection = string.IsNullOrWhiteSpace(arguments.Collection) ? MemoryLayers.Memory : arguments.Collection.Trim();
        var existing = await memoryStore.GetRecordAsync(collection, arguments.Id, cancellationToken);

        if (existing is null)
        {
            return new LocalToolResult($"Memory record {arguments.Id} was not found in {collection}.");
        }

        var action = arguments.Action.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var why = string.IsNullOrWhiteSpace(arguments.Why) ? $"memory_correct {action}" : arguments.Why.Trim();

        if (action == "forget")
        {
            var metadata = existing.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
            metadata[MemoryMetadata.StateKey] = MemoryMetadata.ForgottenState;
            metadata[MemoryMetadata.ValidUntilKey] = now;
            metadata[MemoryMetadata.WhyKey] = why;
            var updated = await memoryStore.UpdateRecordAsync(collection, existing.Id, new MemoryRecordUpdateRequest(existing.Text, metadata), cancellationToken);
            return new LocalToolResult(updated is null
                ? $"Memory record {existing.Id} could not be forgotten."
                : $"Forgot memory {existing.Id}; it remains in history but is suppressed from recall.");
        }

        if (action is not ("edit" or "supersede"))
        {
            return new LocalToolResult("Unsupported memory correction action. Use edit, supersede, or forget.");
        }

        if (string.IsNullOrWhiteSpace(arguments.Text))
        {
            return new LocalToolResult("Replacement text is required for edit or supersede.");
        }

        var oldMetadata = existing.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
        oldMetadata[MemoryMetadata.StateKey] = MemoryMetadata.SupersededState;
        oldMetadata[MemoryMetadata.ValidUntilKey] = now;
        oldMetadata[MemoryMetadata.WhyKey] = why;

        var replacementMetadata = existing.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in arguments.Metadata ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                replacementMetadata[pair.Key] = pair.Value;
            }
        }

        replacementMetadata[MemoryMetadata.StateKey] = MemoryMetadata.ActiveState;
        replacementMetadata[MemoryMetadata.ValidFromKey] = now;
        replacementMetadata.Remove(MemoryMetadata.ValidUntilKey);
        replacementMetadata[MemoryMetadata.SupersedesKey] = existing.Id;
        replacementMetadata[MemoryMetadata.WhyKey] = why;
        replacementMetadata[MemoryMetadata.MergeKey] = oldMetadata.GetValueOrDefault(MemoryMetadata.MergeKey) ?? MemoryMetadata.BuildMergeKey(replacementMetadata, arguments.Text, replacementMetadata.GetValueOrDefault(MemoryMetadata.TypeKey));
        replacementMetadata[MemoryMetadata.ProvenanceKey] = "model_correction";
        replacementMetadata[MemoryMetadata.SourceConversationKey] = invocation.ConversationId ?? replacementMetadata.GetValueOrDefault(MemoryMetadata.SourceConversationKey) ?? string.Empty;
        replacementMetadata["agent"] = invocation.Agent.Name;

        var replacement = await memoryStore.UpsertAsync(new MemoryUpsertRequest(collection, arguments.Text.Trim(), replacementMetadata), cancellationToken);
        oldMetadata[MemoryMetadata.SupersededByKey] = replacement.Id;
        await memoryStore.UpdateRecordAsync(collection, existing.Id, new MemoryRecordUpdateRequest(existing.Text, oldMetadata), cancellationToken);
        return new LocalToolResult($"Superseded memory {existing.Id} with active replacement {replacement.Id} in {collection}.");
    }
}

public sealed record MemoryCorrectArguments(
    [property: Required] string Id,
    string Collection = MemoryLayers.Memory,
    [property: Required] string Action = "supersede",
    string? Text = null,
    string? Why = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
