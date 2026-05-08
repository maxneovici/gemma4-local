using System.Text.RegularExpressions;
using LLLMax.Api.Models;

namespace LLLMax.Api.Memory;

public static class MemoryRecallPolicy
{
    public static IReadOnlyList<string> BuildQueries(string query, IReadOnlyList<LocalChatMessage>? messages = null, MemoryRecallPlan? plan = null)
    {
        var queries = new List<string>();
        AddQuery(queries, NormalizeQuery(query));

        foreach (var part in SplitQuery(query))
        {
            AddQuery(queries, part);
        }

        foreach (var plannedQuery in plan?.Queries ?? [])
        {
            AddQuery(queries, plannedQuery);
        }

        foreach (var facet in plan?.RequiredFacets ?? [])
        {
            AddQuery(queries, facet);
        }

        if (plan?.Keywords is { Count: > 0 } keywords)
        {
            AddQuery(queries, $"{query} {string.Join(' ', keywords.Take(32))}");
        }

        if (messages is not null)
        {
            var context = BuildReferenceContext(query, messages);

            if (!string.IsNullOrWhiteSpace(context))
            {
                AddQuery(queries, context);
            }
        }

        return queries.Count == 0 ? [query] : queries.Take(12).ToList();
    }

    public static bool IsNoiseResult(MemorySearchResult result) =>
        IsNegativeKnowledgeMemory(result.Text)
        || IsUserTurnMemory(result)
        || IsNonKnowledgeKind(result)
        || LooksLikeRawAssistantTranscript(result.Text);

    public static IReadOnlyList<MemorySearchResult> SelectTopDiverse(
        IEnumerable<(MemorySearchResult Result, int Priority, int QueryIndex)> items,
        IReadOnlyList<string> queries,
        MemoryRecallPlan? plan,
        int limit)
    {
        var ranked = items
            .Where(item => !IsNoiseResult(item.Result))
            .GroupBy(item => item.Result.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => Rank(item.Result, item.Priority, queries.ElementAtOrDefault(item.QueryIndex) ?? string.Empty, plan))
                .First())
            .OrderByDescending(item => Rank(item.Result, item.Priority, queries.ElementAtOrDefault(item.QueryIndex) ?? string.Empty, plan))
            .ThenBy(item => item.Priority)
            .ToList();

        var selected = new List<(MemorySearchResult Result, int Priority, int QueryIndex)>();
        var facetTerms = (plan?.RequiredFacets ?? [])
            .Select(facet => ExtractSignificantTerms(facet).ToHashSet(StringComparer.OrdinalIgnoreCase))
            .Where(terms => terms.Count > 0)
            .ToList();

        foreach (var facet in facetTerms)
        {
            var candidate = ranked.FirstOrDefault(item => !selected.Any(selectedItem => selectedItem.Result.Id.Equals(item.Result.Id, StringComparison.OrdinalIgnoreCase))
                && ExtractSignificantTerms(item.Result.Text).Any(facet.Contains));

            if (candidate.Result is not null)
            {
                selected.Add(candidate);

                if (selected.Count >= limit)
                {
                    return selected.Select(item => item.Result).ToList();
                }
            }
        }

        foreach (var item in ranked)
        {
            if (selected.Any(selectedItem => selectedItem.Result.Id.Equals(item.Result.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(item);

            if (selected.Count >= limit)
            {
                break;
            }
        }

        return selected.Select(item => item.Result).ToList();
    }

    public static double Rank(MemorySearchResult result, int priority, string query, MemoryRecallPlan? plan) =>
        result.Score
        + (priority < 0 ? 0.03 : 0)
        + LexicalOverlapBoost(result.Text, query)
        + PlanKeywordBoost(result.Text, plan)
        + MetadataBoost(result)
        - (Math.Max(priority, 0) * 0.02);

    public static bool IsNegativeKnowledgeMemory(string text)
    {
        var lower = text.ToLowerInvariant();
        return ContainsAny(lower,
            "no specific information was provided",
            "no specific information",
            "i do not have specific information",
            "i don't have specific information",
            "do not have any specific information",
            "don't have any specific information",
            "not in my current memory",
            "i don't have that detail",
            "i do not have that detail",
            "i don't know",
            "i do not know",
            "must have hallucinated",
            "seems i must have hallucinated");
    }

    private static void AddQuery(ICollection<string> queries, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var normalized = NormalizeQuery(query);

        if (!queries.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            queries.Add(normalized);
        }
    }

    private static string NormalizeQuery(string query) =>
        Regex.Replace(query, "\\s+", " ").Trim();

    private static IEnumerable<string> SplitQuery(string query)
    {
        var normalized = NormalizeQuery(query);

        foreach (var part in Regex.Split(normalized, @"[?。！？]+|[;,；]+"))
        {
            var trimmed = part.Trim(' ', '.', ',', ';', ':');

            if (trimmed.Length >= 8)
            {
                yield return trimmed;
            }
        }
    }

    private static string BuildReferenceContext(string query, IReadOnlyList<LocalChatMessage> messages)
    {
        var recent = messages
            .Where(message => message.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                || message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .TakeLast(4)
            .Select(message => $"{message.Role}: {TrimForQuery(message.Content)}")
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return $"Current question: {NormalizeQuery(query)} Recent context for reference resolution only: {string.Join(" ", recent)}";
    }

    private static string TrimForQuery(string value)
    {
        var normalized = Regex.Replace(value, "\\s+", " ").Trim();
        return normalized.Length <= 220 ? normalized : normalized[..220];
    }

    private static bool IsUserTurnMemory(MemorySearchResult result) =>
        result.Metadata.TryGetValue("kind", out var kind)
        && kind.Equals("user_turn", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonKnowledgeKind(MemorySearchResult result) =>
        result.Metadata.TryGetValue("kind", out var kind)
        && (kind.Equals("open_loop", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("task_graph", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("consolidated_session", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeRawAssistantTranscript(string text) =>
        text.Contains("\ncoordinator:", StringComparison.OrdinalIgnoreCase)
        || text.Contains("\nassistant:", StringComparison.OrdinalIgnoreCase);

    private static double LexicalOverlapBoost(string text, string query)
    {
        var queryTerms = ExtractSignificantTerms(query).ToList();

        if (queryTerms.Count == 0)
        {
            return 0;
        }

        var textTerms = ExtractSignificantTerms(text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = queryTerms.Count(term => textTerms.Contains(term));
        return Math.Min(0.18, overlap * 0.03);
    }

    private static double PlanKeywordBoost(string text, MemoryRecallPlan? plan)
    {
        var keywords = plan?.Keywords ?? [];
        var matches = keywords.Count(keyword => !string.IsNullOrWhiteSpace(keyword) && text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        return Math.Min(0.24, matches * 0.03);
    }

    private static double MetadataBoost(MemorySearchResult result)
    {
        var boost = 0.0;

        if (MetadataValue(result, "subject") is { Length: > 0 })
        {
            boost += 0.02;
        }

        if (MetadataValue(result, "category") is { Length: > 0 })
        {
            boost += 0.02;
        }

        if (MetadataValue(result, "topic") is { Length: > 0 })
        {
            boost += 0.02;
        }

        if (MetadataValue(result, "kind")?.Equals("canonical_profile_fact", StringComparison.OrdinalIgnoreCase) == true)
        {
            boost += 0.03;
        }

        return boost;
    }

    private static string? MetadataValue(MemorySearchResult result, string key) =>
        result.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static IEnumerable<string> ExtractSignificantTerms(string value) =>
        Regex.Matches(NormalizeQuery(value).ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
            .Select(match => match.Value)
            .Where(term => !StopWords.Contains(term));

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "where", "when", "who", "how", "about", "with", "from", "that", "this", "have", "your", "you", "me", "my", "mine", "any", "the", "and", "for", "are", "was", "were", "like", "remember", "memories", "information", "specific", "specifics"
    };

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
