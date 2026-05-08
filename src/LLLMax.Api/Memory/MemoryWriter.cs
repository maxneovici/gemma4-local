namespace LLLMax.Api.Memory;

public sealed class MemoryWriter(ILocalMemoryStore memoryStore) : IMemoryWriter
{
    public async Task<MemoryUpsertResponse> UpsertOrReinforceAsync(MemoryUpsertRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Collection) || string.IsNullOrWhiteSpace(request.Text))
        {
            return await memoryStore.UpsertAsync(request, cancellationToken);
        }

        var metadata = new Dictionary<string, string>(request.Metadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        if (!metadata.TryGetValue(MemoryMetadata.MergeKey, out var mergeKey) || string.IsNullOrWhiteSpace(mergeKey))
        {
            mergeKey = MemoryMetadata.BuildMergeKey(metadata, request.Text, metadata.GetValueOrDefault(MemoryMetadata.TypeKey));
            metadata[MemoryMetadata.MergeKey] = mergeKey;
        }

        var existing = await FindExistingAsync(request.Collection, mergeKey, cancellationToken);

        if (existing is null)
        {
            return await memoryStore.UpsertAsync(request with { Metadata = metadata }, cancellationToken);
        }

        var mergedMetadata = MergeMetadata(existing.Metadata, metadata);
        var mergedText = PreferBetterText(existing.Text, request.Text);
        await memoryStore.UpdateRecordAsync(request.Collection, existing.Id, new MemoryRecordUpdateRequest(mergedText, mergedMetadata), cancellationToken);
        return new MemoryUpsertResponse(existing.Id, request.Collection);
    }

    public async Task<MemoryBatchUpsertResponse> UpsertOrReinforceBatchAsync(MemoryBatchUpsertRequest request, CancellationToken cancellationToken)
    {
        var ids = new List<string>();

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Text))
            {
                continue;
            }

            var response = await UpsertOrReinforceAsync(new MemoryUpsertRequest(request.Collection, item.Text, item.Metadata), cancellationToken);
            ids.Add(response.Id);
        }

        return new MemoryBatchUpsertResponse(ids, request.Collection);
    }

    private async Task<MemoryRecordDetail?> FindExistingAsync(string collection, string mergeKey, CancellationToken cancellationToken)
    {
        var inspected = await memoryStore.InspectCollectionAsync(collection, new MemoryCollectionInspectRequest(
            Limit: 1,
            Filter: new Dictionary<string, string> { [MemoryMetadata.MergeKey] = mergeKey }), cancellationToken);
        var preview = inspected.Records.FirstOrDefault(record => !MemoryMetadata.IsSuppressedForRecall(record.Metadata));

        return preview is null ? null : await memoryStore.GetRecordAsync(collection, preview.Id, cancellationToken);
    }

    private static Dictionary<string, string> MergeMetadata(IReadOnlyDictionary<string, string> existing, IReadOnlyDictionary<string, string> incoming)
    {
        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in incoming)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!merged.TryGetValue(key, out var oldValue) || string.IsNullOrWhiteSpace(oldValue) || ShouldReplace(key, oldValue, value))
            {
                merged[key] = value;
            }
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        merged[MemoryMetadata.ReinforcedAtKey] = now;
        merged["observedAt"] = now;
        merged[MemoryMetadata.ReinforcementCountKey] = (ParseInt(merged.GetValueOrDefault(MemoryMetadata.ReinforcementCountKey)) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return merged;
    }

    private static bool ShouldReplace(string key, string oldValue, string newValue)
    {
        if (key.Equals(MemoryMetadata.ConfidenceKey, StringComparison.OrdinalIgnoreCase))
        {
            return ParseDouble(newValue) > ParseDouble(oldValue);
        }

        return newValue.Length > oldValue.Length && newValue.Length <= 180;
    }

    private static string PreferBetterText(string existing, string incoming)
    {
        var candidate = incoming.Trim();
        var current = existing.Trim();

        if (candidate.Length > current.Length && candidate.Length <= current.Length + 180)
        {
            return candidate;
        }

        return current;
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static double ParseDouble(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
