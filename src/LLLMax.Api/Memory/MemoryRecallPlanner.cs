using System.Text.Json;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using LLLMax.Api.UserProfile;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Memory;

public interface IMemoryRecallPlanner
{
    Task<MemoryRecallPlan> PlanAsync(string query, IReadOnlyList<LocalChatMessage>? messages, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> RerankAsync(string query, MemoryRecallPlan plan, IReadOnlyList<MemorySearchResult> candidates, int limit, CancellationToken cancellationToken);
}

public sealed record MemoryRecallPlan(
    IReadOnlyList<string> Queries,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> RequiredFacets);

public sealed class MemoryRecallPlanner(
    ILocalChatClient chatClient,
    ILocalMemoryStore memoryStore,
    IFoundationUserProfileStore foundationUserProfile,
    IRuntimeModelSettings runtimeModels,
    IOptions<LocalAiOptions> options) : IMemoryRecallPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalAiOptions _options = options.Value;

    public async Task<MemoryRecallPlan> PlanAsync(string query, IReadOnlyList<LocalChatMessage>? messages, CancellationToken cancellationToken)
    {
        if (!_options.Memory.RecallPlanningEnabled || string.IsNullOrWhiteSpace(query))
        {
            return Empty;
        }

        try
        {
            var recent = messages is null
                ? string.Empty
                : string.Join("\n", messages
                    .Where(message => message.Role.Equals("user", StringComparison.OrdinalIgnoreCase) || message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(4)
                    .Select(message => $"{message.Role}: {Trim(message.Content)}"));
            var profileHints = await LoadProfileRecallHintsAsync(cancellationToken);
            var response = await chatClient.ChatAsync(new LocalChatRequest(
                Model: runtimeModels.GetCoordinatorModel(),
                Messages:
                [
                    new LocalChatMessage("system", "You generate recall search plans for local vector memory. Return exactly one compact JSON object with keys queries, keywords, and requiredFacets. Do not answer the user. Work in the user's language. Split multi-intent questions into separate facet queries. Resolve pronouns or short follow-ups using recent context. Expand abbreviations and infer adjacent facets from the question itself. Use profile memory hints only to form better retrieval queries and facet labels; do not treat them as the final answer. requiredFacets should name each distinct information need in a few words. Keep at most 8 queries, 32 keywords, and 8 requiredFacets. No explanations."),
                    new LocalChatMessage("user", $"Current user query:\n{query}\n\nProfile memory hints for query planning only:\n{string.Join("\n", profileHints)}\n\nRecent context, optional for pronoun/reference resolution:\n{recent}")
                ],
                Temperature: 0.1,
                MaxOutputTokens: 350), cancellationToken);

            return Parse(response.Response);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or JsonException)
        {
            return Empty;
        }
    }

    public async Task<IReadOnlyList<string>> RerankAsync(string query, MemoryRecallPlan plan, IReadOnlyList<MemorySearchResult> candidates, int limit, CancellationToken cancellationToken)
    {
        if (!_options.Memory.RecallRerankingEnabled || candidates.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return candidates.Take(limit).Select(candidate => candidate.Id).ToList();
        }

        try
        {
            var candidateText = string.Join("\n", candidates.Take(40).Select((candidate, index) =>
                $"{index + 1}. id={candidate.Id}; score={candidate.Score:0.000}; metadata={JsonSerializer.Serialize(candidate.Metadata, JsonOptions)}; text={Trim(candidate.Text)}"));
            var response = await chatClient.ChatAsync(new LocalChatRequest(
                Model: runtimeModels.GetCoordinatorModel(),
                Messages:
                [
                    new LocalChatMessage("system", "Select local memory records that best answer the user's recall request. Work in any language. Cover every distinct required facet when possible. Prefer explicit user/profile facts over generic interests, names, raw turns, or adjacent-session noise. Return exactly one compact JSON object: {\"ids\":[...]}. Use only candidate ids. No explanations."),
                    new LocalChatMessage("user", $"User recall request:\n{query}\n\nRequired facets, if any:\n{string.Join("; ", plan.RequiredFacets)}\n\nCandidates:\n{candidateText}\n\nSelect at most {limit} ids in best answer order.")
                ],
                Temperature: 0.0,
                MaxOutputTokens: 260), cancellationToken);
            var json = ExtractJsonObject(response.Response);
            var payload = json is null ? null : JsonSerializer.Deserialize<MemoryRecallRerankPayload>(json, JsonOptions);
            var ids = (payload?.Ids ?? [])
                .Where(id => candidates.Any(candidate => candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();

            return ids.Count == 0 ? candidates.Take(limit).Select(candidate => candidate.Id).ToList() : ids;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or JsonException)
        {
            return candidates.Take(limit).Select(candidate => candidate.Id).ToList();
        }
    }

    private async Task<IReadOnlyList<string>> LoadProfileRecallHintsAsync(CancellationToken cancellationToken)
    {
        var hints = new List<string>();
        var profile = await foundationUserProfile.GetAsync(cancellationToken);

        AddHint(hints, "foundation.fullName", profile.FullName);
        AddHint(hints, "foundation.familyAndRelations", profile.FamilyAndRelations);
        AddHint(hints, "foundation.work", profile.Work);
        AddHint(hints, "foundation.interests", profile.Interests);
        AddHint(hints, "foundation.facts", profile.Facts);

        var inspected = await memoryStore.InspectCollectionAsync(MemoryLayers.Memory, new MemoryCollectionInspectRequest(Limit: 160), cancellationToken);

        foreach (var record in inspected.Records)
        {
            var metadata = string.Join(", ", new[]
            {
                MetadataValue(record.Metadata, "kind") is { } kind ? $"kind={kind}" : null,
                MetadataValue(record.Metadata, "category") is { } category ? $"category={category}" : null,
                MetadataValue(record.Metadata, "topic") is { } topic ? $"topic={topic}" : null,
                MetadataValue(record.Metadata, "subject") is { } subject ? $"subject={subject}" : null
            }.Where(item => item is not null));
            AddHint(hints, string.IsNullOrWhiteSpace(metadata) ? MemoryLayers.Memory : $"{MemoryLayers.Memory}; {metadata}", record.TextPreview);
        }

        return hints
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
    }

    private static void AddHint(ICollection<string> hints, string label, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        hints.Add($"- {label}: {(normalized.Length <= 220 ? normalized : normalized[..220])}");
    }

    private static string? MetadataValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static MemoryRecallPlan Parse(string text)
    {
        var json = ExtractJsonObject(text);

        if (json is null)
        {
            return Empty;
        }

        var plan = JsonSerializer.Deserialize<MemoryRecallPlan>(json, JsonOptions);

        if (plan is null)
        {
            return Empty;
        }

        return new MemoryRecallPlan(
            Clean(plan.Queries, 8),
            Clean(plan.Keywords, 32),
            Clean(plan.RequiredFacets, 8));
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values, int limit) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string Trim(string value) => value.Length <= 400 ? value : value[..400];

    private static MemoryRecallPlan Empty => new([], [], []);

    private sealed record MemoryRecallRerankPayload(IReadOnlyList<string>? Ids);
}
