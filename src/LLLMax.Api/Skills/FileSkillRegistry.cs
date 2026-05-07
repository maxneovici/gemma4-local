using LLLMax.Api.Services;
using System.Text.RegularExpressions;

namespace LLLMax.Api.Skills;

public sealed class FileSkillRegistry(LocalDataPaths paths) : ISkillRegistry
{
    public IReadOnlyList<SkillDefinition> GetSkills() =>
        Directory.EnumerateFiles(paths.SkillsDirectory, "*.md")
            .Select(ParseSkill)
            .OfType<SkillDefinition>()
            .OrderByDescending(skill => skill.Priority)
            .ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public SkillDefinition GetRequiredSkill(string name) =>
        GetSkills().SingleOrDefault(skill => string.Equals(skill.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Skill '{name}' is not configured.");

    public IReadOnlyList<SkillDefinition> FindRelevant(string agent, string message, int limit)
    {
        var terms = Tokenize(message).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetSkills()
            .Where(skill => skill.Enabled && IsAgentMatch(skill, agent))
            .Select(skill => new { Skill = skill, Score = Score(skill, terms, message) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Skill.Priority)
            .Take(Math.Max(0, limit))
            .Select(item => item.Skill)
            .ToList();
    }

    private static SkillDefinition? ParseSkill(string file)
    {
        var text = File.ReadAllText(file);

        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return null;
        }

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);

        if (end < 0)
        {
            return null;
        }

        var metadata = ParseMetadata(text[3..end]);
        var body = text[(end + 5)..].Trim();

        if (!metadata.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return new SkillDefinition(
            Name: name,
            Description: metadata.GetValueOrDefault("description") ?? $"Skill {name}.",
            Triggers: SplitList(metadata.GetValueOrDefault("triggers")),
            Agents: SplitList(metadata.GetValueOrDefault("agents")),
            Priority: int.TryParse(metadata.GetValueOrDefault("priority"), out var priority) ? priority : 50,
            Enabled: !bool.TryParse(metadata.GetValueOrDefault("enabled"), out var enabled) || enabled,
            Body: body,
            FileName: Path.GetFileName(file));
    }

    private static Dictionary<string, string> ParseMetadata(string frontmatter)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var currentItems = new List<string>();

        foreach (var rawLine in frontmatter.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal) && currentKey is not null)
            {
                currentItems.Add(line.TrimStart()[2..].Trim());
                continue;
            }

            FlushList();
            var parts = line.Split(':', 2);

            if (parts.Length != 2)
            {
                continue;
            }

            currentKey = parts[0].Trim();
            var value = parts[1].Trim();

            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[currentKey] = value;
                currentKey = null;
            }
        }

        FlushList();
        return metadata;

        void FlushList()
        {
            if (currentKey is not null && currentItems.Count > 0)
            {
                metadata[currentKey] = string.Join(',', currentItems);
            }

            currentKey = null;
            currentItems.Clear();
        }
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsAgentMatch(SkillDefinition skill, string agent) =>
        skill.Agents.Count == 0
        || skill.Agents.Contains("*", StringComparer.OrdinalIgnoreCase)
        || skill.Agents.Contains(agent, StringComparer.OrdinalIgnoreCase);

    private static int Score(SkillDefinition skill, IReadOnlySet<string> terms, string message)
    {
        var score = 0;

        foreach (var trigger in skill.Triggers)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                continue;
            }

            var normalized = trigger.Trim();
            score += IsTriggerMatch(message, normalized) ? 5 : 0;
            score += Tokenize(normalized).Count(terms.Contains);
        }

        return score == 0 ? 0 : score + Math.Clamp(skill.Priority / 25, 0, 4);
    }

    private static bool IsTriggerMatch(string message, string trigger)
    {
        var pattern = $"(?<![A-Za-z0-9]){Regex.Escape(trigger).Replace("\\ ", "\\s+")}(?![A-Za-z0-9])";
        return Regex.IsMatch(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<string> Tokenize(string text) =>
        text.Split([' ', '\t', '\r', '\n', '.', ',', ':', ';', '!', '?', '/', '\\', '-', '_', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .ToList();
}
