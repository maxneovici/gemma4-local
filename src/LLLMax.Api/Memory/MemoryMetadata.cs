using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LLLMax.Api.Memory;

public static class MemoryMetadata
{
    public const string TypeKey = "memoryType";
    public const string ProvenanceKey = "provenance";
    public const string ConfidenceKey = "confidence";
    public const string MergeKey = "mergeKey";
    public const string ReviewStatusKey = "reviewStatus";
    public const string SourceConversationKey = "sourceConversationId";
    public const string SourceMessageRoleKey = "sourceMessageRole";
    public const string SourceRecordIdKey = "sourceRecordId";
    public const string SupersedesKey = "supersedes";
    public const string SupersededByKey = "supersededBy";

    public static Dictionary<string, string> Build(
        IReadOnlyDictionary<string, string>? metadata,
        string layer,
        string text,
        string provenance,
        string? memoryType = null,
        string? conversationId = null,
        string? sourceMessageRole = null,
        string? sourceRecordId = null,
        double confidence = 0.7,
        bool reviewRequired = false)
    {
        var result = MemoryLayers.WithLayer(metadata, layer);
        var type = NormalizeType(memoryType ?? result.GetValueOrDefault(TypeKey) ?? result.GetValueOrDefault("category") ?? result.GetValueOrDefault("kind") ?? "fact");
        result[TypeKey] = type;
        result[ProvenanceKey] = provenance;
        result[ConfidenceKey] = confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        result[MergeKey] = BuildMergeKey(result, text, type);
        result[ReviewStatusKey] = reviewRequired ? "pending" : result.GetValueOrDefault(ReviewStatusKey) ?? "accepted";

        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            result[SourceConversationKey] = conversationId.Trim();
            result.TryAdd("conversationId", conversationId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sourceMessageRole))
        {
            result[SourceMessageRoleKey] = sourceMessageRole.Trim();
        }

        if (!string.IsNullOrWhiteSpace(sourceRecordId))
        {
            result[SourceRecordIdKey] = sourceRecordId.Trim();
        }

        result.TryAdd("observedAt", DateTimeOffset.UtcNow.ToString("O"));
        return result;
    }

    public static string NormalizeType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "identity" or "personal_identity" or "profile_identity" => "identity",
            "relationship" or "relations" or "family" or "personal_details" => "relationship",
            "preference" or "preferences" or "ranking_criteria" or "communication_style" => "preference",
            "opinion" or "opinions" => "opinion",
            "interest" or "interests" or "hobby" or "hobbies" => "interest",
            "goal" or "goals" or "open_loop" or "follow_up" => "goal",
            "project" or "projects" or "current_project" or "workflow" => "project",
            "constraint" or "constraints" => "constraint",
            "document_chunk" or "knowledge" or "reference" => "knowledge",
            "session_summary" or "consolidated_session" => "summary",
            "memory_category" or "memory_schema" => "schema",
            _ => "fact"
        };
    }

    public static string BuildMergeKey(IReadOnlyDictionary<string, string> metadata, string text, string? memoryType = null)
    {
        var type = NormalizeType(memoryType ?? metadata.GetValueOrDefault(TypeKey) ?? metadata.GetValueOrDefault("category") ?? "fact");
        var subject = metadata.GetValueOrDefault("subject") ?? "user";
        var topic = metadata.GetValueOrDefault("topic") ?? metadata.GetValueOrDefault("project") ?? ExtractKeyPhrase(text);
        return StableKey($"{subject}|{type}|{topic}|{NormalizeForKey(text)}");
    }

    public static string StableKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];

    private static string ExtractKeyPhrase(string text)
    {
        var terms = Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{4,}")
            .Select(match => match.Value)
            .Where(term => !StopWords.Contains(term))
            .Take(6);
        var phrase = string.Join(' ', terms);
        return string.IsNullOrWhiteSpace(phrase) ? "general" : phrase;
    }

    private static string NormalizeForKey(string value)
    {
        var terms = Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}]{4,}")
            .Select(match => match.Value)
            .Where(term => !StopWords.Contains(term))
            .Take(18);
        return string.Join(' ', terms);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "again", "also", "because", "before", "being", "could", "from", "have", "into", "local", "memory", "more", "only", "over", "prefer", "prefers", "should", "that", "their", "there", "these", "this", "user", "when", "with", "would"
    };
}
