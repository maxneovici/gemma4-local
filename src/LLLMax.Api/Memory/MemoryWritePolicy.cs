namespace LLLMax.Api.Memory;

public static class MemoryWritePolicy
{
    public static bool ShouldSkipProfileMemory(string text, IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var memoryType = metadata?.GetValueOrDefault(MemoryMetadata.TypeKey);
        var kind = metadata?.GetValueOrDefault("kind");

        if (IsAssistantOperationalGoal(text)
            && (MemoryMetadata.NormalizeType(memoryType ?? "goal") == "goal"
                || kind?.Contains("profile", StringComparison.OrdinalIgnoreCase) == true))
        {
            return true;
        }

        return false;
    }

    private static bool IsAssistantOperationalGoal(string text)
    {
        var lower = text.ToLowerInvariant();
        return ContainsAny(lower, "memory graph", "knowledge graph", "canonical profile", "profile memory", "memory pipeline", "memory system")
            && ContainsAny(lower, "build", "map", "mapping", "improve", "clean", "reliable", "deduplicate", "deduplicated", "seed", "reflect", "consolidate");
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
