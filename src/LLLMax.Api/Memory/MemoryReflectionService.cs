using System.Text.Json;
using LLLMax.Api.Models;
using LLLMax.Api.Services;

namespace LLLMax.Api.Memory;

public sealed class MemoryReflectionService(
    ILocalMemoryStore memoryStore,
    ILocalChatClient chatClient,
    IRuntimeModelSettings runtimeModels) : IMemoryReflectionService
{
    private const string CanonicalCollection = MemoryLayers.Memory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MemoryReflectionResponse> ReflectAsync(MemoryReflectionRequest request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 20, 200);
        var source = await LoadProfileSourceAsync(limit, cancellationToken);

        if (source.Count == 0)
        {
            return new MemoryReflectionResponse(CanonicalCollection, 0, 0, 0, "No profile memories found to reflect.");
        }

        var payload = await BuildReflectionPayloadAsync(source, request.Model, cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var writtenFacts = 0;
        var writtenCategories = 0;
        var existingProfile = await GetProfileAsync(cancellationToken);

        foreach (var fact in (payload.Facts ?? []).Where(fact => !string.IsNullOrWhiteSpace(fact.Text)).Take(60))
        {
            var text = fact.Text.Trim();
            var category = string.IsNullOrWhiteSpace(fact.Category) ? "profile" : fact.Category.Trim();
            var topic = fact.Topic?.Trim() ?? string.Empty;
            var subject = string.IsNullOrWhiteSpace(fact.Subject) ? "user" : fact.Subject.Trim();
            var existing = FindMatchingFact(existingProfile.Facts, text, category, topic, subject);
            var mergeKey = MergeKey(subject, category, topic, text);
            var metadata = MemoryMetadata.Build(new Dictionary<string, string>
            {
                ["kind"] = "canonical_profile_fact",
                ["category"] = category,
                ["topic"] = topic,
                ["subject"] = subject,
                ["source"] = "memory_reflection",
                ["mergeKey"] = mergeKey
            }, MemoryLayers.Memory, text, "memory_reflection", category, sourceRecordId: existing?.Id, confidence: fact.Confidence ?? existing?.Confidence ?? 0.7);

            if (existing is null)
            {
                await memoryStore.UpsertAsync(new MemoryUpsertRequest(CanonicalCollection, text, metadata), cancellationToken);
            }
            else
            {
                await memoryStore.UpdateRecordAsync(CanonicalCollection, existing.Id, new MemoryRecordUpdateRequest(BetterFactText(existing.Text, text), metadata), cancellationToken);
            }

            writtenFacts++;
        }

        foreach (var category in (payload.Categories ?? []).Where(category => !string.IsNullOrWhiteSpace(category.Name)).Take(40))
        {
            var examples = category.Examples is { Count: > 0 }
                ? $" Examples: {string.Join("; ", category.Examples.Take(5))}"
                : string.Empty;
            var name = category.Name.Trim();
            var text = $"Memory category '{name}': {category.Description.Trim()}{examples}";
            var existing = existingProfile.Categories.LastOrDefault(existing => existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var categoryRecords = await memoryStore.InspectCollectionAsync(CanonicalCollection, new MemoryCollectionInspectRequest(
                Limit: 20,
                Filter: new Dictionary<string, string>
                {
                    ["kind"] = "memory_category",
                    ["topic"] = name
                }), cancellationToken);
            var metadata = MemoryMetadata.Build(new Dictionary<string, string>
            {
                ["kind"] = "memory_category",
                ["category"] = "memory_schema",
                ["topic"] = name,
                ["subject"] = "memory_graph",
                ["source"] = "memory_reflection"
            }, MemoryLayers.Memory, text, "memory_reflection", "schema", confidence: 0.7);

            if (categoryRecords.Records.FirstOrDefault() is { } existingRecord)
            {
                await memoryStore.UpdateRecordAsync(CanonicalCollection, existingRecord.Id, new MemoryRecordUpdateRequest(text, metadata), cancellationToken);
            }
            else if (existing is null || string.IsNullOrWhiteSpace(existing.Description))
            {
                await memoryStore.UpsertAsync(new MemoryUpsertRequest(CanonicalCollection, text, metadata), cancellationToken);
            }

            writtenCategories++;
        }

        return new MemoryReflectionResponse(CanonicalCollection, source.Count, writtenFacts, writtenCategories, payload.Summary.Trim());
    }

    public async Task<MemoryProfileResponse> GetProfileAsync(CancellationToken cancellationToken)
    {
        var inspected = await memoryStore.InspectCollectionAsync(CanonicalCollection, new MemoryCollectionInspectRequest(Limit: 100), cancellationToken);
        var facts = inspected.Records
            .Where(record => MetadataValue(record.Metadata, "kind")?.Equals("canonical_profile_fact", StringComparison.OrdinalIgnoreCase) == true)
            .Select(record => new MemoryProfileFact(
                record.Id,
                record.TextPreview,
                MetadataValue(record.Metadata, "category") ?? "profile",
                EmptyToNull(MetadataValue(record.Metadata, "topic")),
                EmptyToNull(MetadataValue(record.Metadata, "subject")),
                double.TryParse(MetadataValue(record.Metadata, "confidence"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var confidence) ? confidence : null,
                record.Metadata))
            .ToList();
        var schemaCategories = inspected.Records
            .Where(record => MetadataValue(record.Metadata, "kind")?.Equals("memory_category", StringComparison.OrdinalIgnoreCase) == true)
            .Select(record => new MemoryProfileCategory(MetadataValue(record.Metadata, "topic") ?? "uncategorized", 0, record.TextPreview))
            .ToList();
        var categories = facts
            .GroupBy(fact => fact.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MemoryProfileCategory(group.Key, group.Count(), schemaCategories.LastOrDefault(category => category.Name.Equals(group.Key, StringComparison.OrdinalIgnoreCase))?.Description))
            .OrderByDescending(category => category.Count)
            .ThenBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var reflectedAt = inspected.Records
            .Select(record => DateTimeOffset.TryParse(MetadataValue(record.Metadata, "observedAt"), out var parsed) ? parsed : (DateTimeOffset?)null)
            .Where(value => value is not null)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        var summary = facts.Count == 0
            ? "No canonical profile has been reflected yet."
            : $"Canonical profile has {facts.Count} facts across {categories.Count} categories.";

        return new MemoryProfileResponse(CanonicalCollection, summary, facts.Count, schemaCategories.Count, reflectedAt, facts, categories);
    }

    private async Task<IReadOnlyList<MemoryCollectionRecordPreview>> LoadProfileSourceAsync(int limit, CancellationToken cancellationToken)
    {
        var records = new List<MemoryCollectionRecordPreview>();
        var coreFacts = await memoryStore.InspectCollectionAsync(MemoryLayers.Memory, new MemoryCollectionInspectRequest(
            Limit: limit,
            Filter: new Dictionary<string, string> { ["kind"] = "core_memory" }), cancellationToken);
        records.AddRange(coreFacts.Records.Where(record => MetadataValue(record.Metadata, "category")?.Equals("profile", StringComparison.OrdinalIgnoreCase) == true));

        var core = await memoryStore.InspectCollectionAsync(MemoryLayers.Memory, new MemoryCollectionInspectRequest(
            Limit: limit,
            Filter: new Dictionary<string, string> { ["category"] = "profile" }), cancellationToken);
        records.AddRange(core.Records);

        return records
            .GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(limit)
            .ToList();
    }

    private async Task<MemoryReflectionPayload> BuildReflectionPayloadAsync(IReadOnlyList<MemoryCollectionRecordPreview> source, string? model, CancellationToken cancellationToken)
    {
        var sourceText = string.Join("\n", source.Select(record => $"- id={record.Id}; metadata={JsonSerializer.Serialize(record.Metadata, JsonOptions)}; text={record.TextPreview}"));
        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: model ?? runtimeModels.GetCoordinatorModel(),
            Messages:
            [
                new LocalChatMessage("system", "Reflect on local profile memories and produce a canonical user profile. Return exactly one JSON object with summary, facts, and categories. Facts must be explicit or strongly repeated; related facts can coexist. Do not invent, do not resolve ambiguity unless source says so, and do not store secrets. Categories are suggested graph dimensions the assistant can use later."),
                new LocalChatMessage("user", $"Profile memory source records:\n{sourceText}\n\nJSON shape: {{\"summary\":\"...\",\"facts\":[{{\"text\":\"...\",\"category\":\"gaming\",\"topic\":\"path_of_exile\",\"subject\":\"user\",\"confidence\":0.9}}],\"categories\":[{{\"name\":\"gaming\",\"description\":\"...\",\"examples\":[\"...\"]}}]}}")
            ],
            Temperature: 0.1), cancellationToken);

        var json = ExtractJsonObject(response.Response);

        if (json is not null)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<MemoryReflectionPayload>(json, JsonOptions);

                if (payload is not null && !string.IsNullOrWhiteSpace(payload.Summary))
                {
                    return payload;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new MemoryReflectionPayload(response.Response.Trim());
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string? MetadataValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static MemoryProfileFact? FindMatchingFact(IReadOnlyList<MemoryProfileFact> existing, string text, string category, string topic, string subject)
    {
        var key = MergeKey(subject, category, topic, text);
        return existing.FirstOrDefault(fact => MetadataValue(fact.Metadata, "mergeKey")?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
            ?? (!string.IsNullOrWhiteSpace(topic)
                ? existing.FirstOrDefault(fact => fact.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(fact.Topic ?? string.Empty, topic, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(fact.Subject ?? "user", subject, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? existing.FirstOrDefault(fact => fact.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fact.Topic ?? string.Empty, topic, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fact.Subject ?? "user", subject, StringComparison.OrdinalIgnoreCase)
                && SimilarFact(fact.Text, text));
    }

    private static bool SimilarFact(string left, string right)
    {
        var leftTokens = TokenSet(left);
        var rightTokens = TokenSet(right);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return false;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return intersection / (double)union >= 0.72;
    }

    private static HashSet<string> TokenSet(string value) =>
        value.ToLowerInvariant()
            .Split(new[] { ' ', '.', ',', ';', ':', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string BetterFactText(string existing, string candidate) =>
        candidate.Length > existing.Length && candidate.Length <= existing.Length + 140 ? candidate : existing;

    private static string MergeKey(string subject, string category, string topic, string text) =>
        $"{NormalizeKey(subject)}:{NormalizeKey(category)}:{NormalizeKey(string.IsNullOrWhiteSpace(topic) ? "general" : topic)}";

    private static string CanonicalKey(string subject, string category, string topic, string text)
    {
        var normalizedText = string.Join(' ', TokenSet(text).OrderBy(token => token, StringComparer.OrdinalIgnoreCase).Take(16));
        return $"{NormalizeKey(subject)}:{NormalizeKey(category)}:{NormalizeKey(topic)}:{NormalizeKey(normalizedText)}";
    }

    private static string NormalizeKey(string value) =>
        string.Join('_', value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
