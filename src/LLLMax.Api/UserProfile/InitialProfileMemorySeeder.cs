using System.Text.Json;
using LLLMax.Api.Memory;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.UserProfile;

public interface IInitialProfileMemorySeeder
{
    Task<InitialProfileMemorySeedResponse> SeedAsync(FoundationUserProfile profile, CancellationToken cancellationToken);
}

public sealed class InitialProfileMemorySeeder(
    ILocalChatClient chatClient,
    IMemoryWriter memoryWriter,
    IOptions<LocalAiOptions> options) : IInitialProfileMemorySeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalAiOptions _options = options.Value;

    public async Task<InitialProfileMemorySeedResponse> SeedAsync(FoundationUserProfile profile, CancellationToken cancellationToken)
    {
        var source = FoundationUserProfileFormatter.FormatForPrompt(profile);

        if (string.IsNullOrWhiteSpace(source))
        {
            return new InitialProfileMemorySeedResponse(0, 0, 0, 0, "No foundation profile data to seed.");
        }

        var payload = await BuildSeedPayloadAsync(source, cancellationToken);
        var items = new List<MemoryUpsertItem>();

        foreach (var fact in (payload.Facts ?? []).Where(fact => !string.IsNullOrWhiteSpace(fact.Text)).Take(80))
        {
            var text = fact.Text.Trim();
            var subcategory = NormalizeSubcategory(fact.Category, fact.MemoryType);
            var memoryType = NormalizeMemoryType(fact.MemoryType, subcategory);
            var topic = NormalizeKeyValue(fact.Topic) ?? TopicFromText(text);
            var subject = NormalizeKeyValue(fact.Subject) ?? "user";
            var relation = NormalizeKeyValue(fact.Relation);
            var relatedTo = NormalizeKeyValue(fact.RelatedTo);
            var metadata = new Dictionary<string, string>
            {
                ["kind"] = "canonical_profile_fact",
                ["category"] = "profile",
                ["subcategory"] = subcategory,
                ["topic"] = topic,
                ["subject"] = subject,
                ["source"] = "initial_setup",
                ["initialSetup"] = "true"
            };

            if (!string.IsNullOrWhiteSpace(relation)) metadata["relation"] = relation;
            if (!string.IsNullOrWhiteSpace(relatedTo)) metadata["relatedTo"] = relatedTo;

            var builtMetadata = MemoryMetadata.Build(
                metadata,
                MemoryLayers.Memory,
                text,
                "initial_setup",
                memoryType,
                sourceMessageRole: "user",
                confidence: Math.Clamp(fact.Confidence ?? 0.86, 0, 1));

            if (!MemoryWritePolicy.ShouldSkipProfileMemory(text, builtMetadata))
            {
                items.Add(new MemoryUpsertItem(text, builtMetadata));
            }
        }

        foreach (var relationship in (payload.Relationships ?? []).Where(relationship => !string.IsNullOrWhiteSpace(relationship.Name)).Take(40))
        {
            var name = relationship.Name.Trim();
            var relation = NormalizeKeyValue(relationship.Relation) ?? "related_to";
            var detail = string.IsNullOrWhiteSpace(relationship.Details) ? string.Empty : $" {relationship.Details.Trim()}";
            var text = $"{FormatRelationshipText(name, relation)}{detail}".Trim();
            var topic = NormalizeKeyValue(name) ?? "relationship";
            var metadata = new Dictionary<string, string>
            {
                ["kind"] = "canonical_profile_fact",
                ["category"] = "profile",
                ["subcategory"] = "relationships",
                ["topic"] = topic,
                ["subject"] = name,
                ["relation"] = relation,
                ["relatedTo"] = "user",
                ["source"] = "initial_setup",
                ["initialSetup"] = "true"
            };

            items.Add(new MemoryUpsertItem(text, MemoryMetadata.Build(
                metadata,
                MemoryLayers.Memory,
                text,
                "initial_setup",
                "relationship",
                sourceMessageRole: "user",
                confidence: Math.Clamp(relationship.Confidence ?? 0.84, 0, 1))));
        }

        var categories = BuildSeedCategories(payload);

        foreach (var category in categories.Take(32))
        {
            var name = NormalizeSubcategory(category.Name, null);
            var examples = category.Examples is { Count: > 0 }
                ? $" Examples: {string.Join("; ", category.Examples.Where(example => !string.IsNullOrWhiteSpace(example)).Take(5))}"
                : string.Empty;
            var description = string.IsNullOrWhiteSpace(category.Description)
                ? $"Initial setup category for {name}."
                : category.Description.Trim();
            var text = $"Memory category '{name}': {description}{examples}";
            var metadata = new Dictionary<string, string>
            {
                ["kind"] = "memory_category",
                ["category"] = "memory_schema",
                ["subcategory"] = name,
                ["topic"] = name,
                ["subject"] = "memory_graph",
                ["source"] = "initial_setup",
                ["initialSetup"] = "true"
            };

            items.Add(new MemoryUpsertItem(text, MemoryMetadata.Build(
                metadata,
                MemoryLayers.Memory,
                text,
                "initial_setup",
                "schema",
                confidence: 0.8)));
        }

        var response = items.Count == 0
            ? new MemoryBatchUpsertResponse([], MemoryLayers.Memory)
            : await memoryWriter.UpsertOrReinforceBatchAsync(new MemoryBatchUpsertRequest(MemoryLayers.Memory, items), cancellationToken);

        return new InitialProfileMemorySeedResponse(
            response.Ids.Count,
            items.Count(item => GetMetadata(item.Metadata, "kind") == "canonical_profile_fact"),
            (payload.Relationships ?? []).Count,
            categories.Count,
            string.IsNullOrWhiteSpace(payload.Summary) ? "Initial setup seeded foundation profile memory." : payload.Summary.Trim());
    }

    private async Task<InitialProfileMemorySeedPayload> BuildSeedPayloadAsync(string source, CancellationToken cancellationToken)
    {
        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: SelectPowerfulModel(),
            Messages:
            [
                new LocalChatMessage("system", "Canonicalize explicit setup-wizard user data into durable local memory. Return exactly one JSON object with summary, facts, relationships, and categories. memoryType is the primary recall axis and must be one of identity, relationship, preference, opinion, interest, goal, project, constraint, fact. category is only a secondary subcategory label; never rely on it instead of memoryType. Extract stable identity, relationships, interests, preferences, goals, projects, work context, constraints, location, communication style, and other durable facts. Build relation edges for every family member, pet, close relation, workplace/project relationship, and named entity when the source supports it. Do not invent facts, secrets, or sensitive details. Useful subcategories include family, pets, gaming, music, cooking, work, location, communication, goals, privacy."),
                new LocalChatMessage("user", $"Foundation profile source:\n{source}\n\nJSON shape: {{\"summary\":\"...\",\"facts\":[{{\"text\":\"User prefers concise answers.\",\"category\":\"communication\",\"topic\":\"communication_style\",\"subject\":\"user\",\"memoryType\":\"preference\",\"confidence\":0.9}}],\"relationships\":[{{\"name\":\"named person\",\"relation\":\"spouse\",\"details\":\"Household context.\",\"confidence\":0.9}}],\"categories\":[{{\"name\":\"family\",\"description\":\"Relationship subcategory for family members connected to the user.\",\"examples\":[\"spouse\",\"pet\"]}}]}}")
            ],
            Temperature: 0.05,
            MaxOutputTokens: 2200), cancellationToken);

        var json = ExtractJsonObject(response.Response);

        if (json is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<InitialProfileMemorySeedPayload>(json, JsonOptions) ?? new InitialProfileMemorySeedPayload(response.Response.Trim());
            }
            catch (JsonException)
            {
            }
        }

        return FallbackSeedPayload(source, response.Response);
    }

    private string SelectPowerfulModel()
    {
        var configured = _options.RequiredModels
            .Where(model => model.Kind.Equals("chat", StringComparison.OrdinalIgnoreCase))
            .Select(model => model.Name)
            .ToList();

        return configured.FirstOrDefault(model => model.Contains("31b", StringComparison.OrdinalIgnoreCase))
            ?? configured.LastOrDefault()
            ?? _options.Models.CoordinatorModel
            ?? _options.DefaultModel;
    }

    private static InitialProfileMemorySeedPayload FallbackSeedPayload(string source, string summary)
    {
        var facts = source.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..])
            .Select(line =>
            {
                var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
                var label = parts.ElementAtOrDefault(0) ?? "fact";
                var value = parts.ElementAtOrDefault(1) ?? line;
                var subcategory = SubcategoryFromProfileLabel(label);
                return new InitialProfileFact($"User {label}: {value}", subcategory, NormalizeKeyValue(label), "user", NormalizeMemoryType(null, subcategory), null, null, 0.72);
            })
            .ToList();

        return new InitialProfileMemorySeedPayload(string.IsNullOrWhiteSpace(summary) ? "Initial setup seeded from explicit profile fields." : summary.Trim(), facts);
    }

    private static IReadOnlyList<MemoryCategorySuggestion> BuildSeedCategories(InitialProfileMemorySeedPayload payload)
    {
        var categories = new List<MemoryCategorySuggestion>
        {
            new("identity", "Identity subcategory for names and stable identity details about the user.", ["username", "full name"]),
            new("family", "Relationship subcategory for family, household, pets, and close people connected to the user.", ["family", "pet", "colleague"]),
            new("interests", "Interest subcategory for recurring hobbies and domains the user cares about.", ["music", "games", "research"]),
            new("preferences", "Preference subcategory for stable defaults and likes.", ["tone", "format", "likes"]),
            new("goals", "Goal subcategory for near-term and long-term user goals.", ["plans", "objectives"]),
            new("projects", "Project subcategory for work, personal projects, and active initiatives.", ["work", "side project"]),
            new("work", "Job, responsibilities, and professional context.", ["role", "company", "responsibilities"]),
            new("location", "Durable location and timezone context.", ["city", "timezone"]),
            new("communication", "How the user wants the assistant to communicate.", ["direct", "concise", "detailed"]),
            new("constraints", "Privacy, safety, budget, time, and local-only constraints.", ["local-only", "privacy"]),
            new("facts", "Other stable profile facts that do not fit a narrower category.", ["details"])
        };

        categories.AddRange((payload.Categories ?? []).Where(category => !string.IsNullOrWhiteSpace(category.Name)));
        categories.AddRange((payload.Facts ?? [])
            .Where(fact => !string.IsNullOrWhiteSpace(fact.Category))
            .Select(fact => new MemoryCategorySuggestion(NormalizeSubcategory(fact.Category, fact.MemoryType), $"Initial setup facts tagged with subcategory {NormalizeSubcategory(fact.Category, fact.MemoryType)} under memoryType {NormalizeMemoryType(fact.MemoryType, fact.Category)}.")));

        if ((payload.Relationships ?? []).Count > 0)
        {
            categories.Add(new MemoryCategorySuggestion("relationships", "Relationship subcategory for named edges extracted from the setup wizard.", (payload.Relationships ?? []).Select(relationship => relationship.Relation).Where(value => !string.IsNullOrWhiteSpace(value)).Take(8).ToList()));
        }

        return categories
            .GroupBy(category => NormalizeSubcategory(category.Name, null), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { Name = NormalizeSubcategory(group.Key, null) })
            .ToList();
    }

    private static string NormalizeSubcategory(string? category, string? memoryType)
    {
        var value = NormalizeKeyValue(category) ?? NormalizeMemoryType(memoryType, "facts");
        return value switch
        {
            "relationship" or "relations" or "family_and_relations" => "relationships",
            "preference" or "communication_style" => "preferences",
            "interest" => "interests",
            "goal" => "goals",
            "project" => "projects",
            "constraint" => "constraints",
            "profile" or "personal" => "general",
            _ => value
        };
    }

    private static string NormalizeMemoryType(string? memoryType, string category)
    {
        var normalized = MemoryMetadata.NormalizeType(memoryType ?? category);
        return normalized == "fact" && category is "relationships" ? "relationship" : normalized;
    }

    private static string SubcategoryFromProfileLabel(string label) => NormalizeKeyValue(label) switch
    {
        "username" or "email" or "full_name" => "identity",
        "family_and_relations" => "relationships",
        "work" => "work",
        "location" => "location",
        "communication_style" => "communication",
        "interests" => "interests",
        "goals" => "goals",
        "constraints" => "constraints",
        _ => "facts"
    };

    private static string? NormalizeKeyValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join('_', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string TopicFromText(string text)
    {
        var words = text.ToLowerInvariant()
            .Split([' ', '.', ',', ';', ':', '/', '\\', '-', '_', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length > 3)
            .Where(word => word is not "user" and not "prefers" and not "likes" and not "about")
            .Take(4);
        var topic = string.Join('_', words);
        return string.IsNullOrWhiteSpace(topic) ? "general" : topic;
    }

    private static string FormatRelationshipText(string name, string relation)
    {
        var label = relation.Replace('_', ' ');

        return relation switch
        {
            "related_to" => $"{name} is related to the user.",
            "works_with" => $"{name} works with the user.",
            "reports_to" => $"{name} reports to the user.",
            "managed_by" => $"{name} is managed by the user.",
            "collaborates_with" => $"{name} collaborates with the user.",
            "close_relation" => $"{name} is connected to the user as a close relation.",
            _ when IsPossessiveRelation(relation) => $"{name} is the user's {label}.",
            _ => $"{name} is connected to the user as {ArticleFor(label)} {label}."
        };
    }

    private static bool IsPossessiveRelation(string relation) => relation is
        "partner" or "spouse" or "wife" or "husband" or "fiance" or "fiancee" or
        "mother" or "father" or "parent" or "brother" or "sister" or "sibling" or
        "daughter" or "son" or "child" or "friend" or "coworker" or "colleague" or
        "pet" or "cat" or "dog" or "manager" or "employee" or "teammate";

    private static string ArticleFor(string label)
    {
        var first = label.FirstOrDefault();
        return first is 'a' or 'e' or 'i' or 'o' or 'u' ? "an" : "a";
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null && metadata.TryGetValue(key, out var value) ? value : null;
}

public sealed record InitialProfileMemorySeedResponse(int MemoriesWritten, int FactsWritten, int RelationshipsWritten, int CategoriesWritten, string Summary);

public sealed record InitialProfileMemorySeedPayload(
    string Summary,
    IReadOnlyList<InitialProfileFact>? Facts = null,
    IReadOnlyList<InitialProfileRelationship>? Relationships = null,
    IReadOnlyList<MemoryCategorySuggestion>? Categories = null);

public sealed record InitialProfileFact(
    string Text,
    string Category = "facts",
    string? Topic = null,
    string Subject = "user",
    string? MemoryType = null,
    string? Relation = null,
    string? RelatedTo = null,
    double? Confidence = null);

public sealed record InitialProfileRelationship(
    string Name,
    string Relation,
    string? Details = null,
    double? Confidence = null);
