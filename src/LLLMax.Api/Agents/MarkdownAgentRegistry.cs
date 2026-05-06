using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Agents;

public sealed class MarkdownAgentRegistry(IOptions<LocalAiOptions> options, LocalDataPaths paths) : IAgentRegistry
{
    private readonly LocalAiOptions _options = options.Value;

    public IReadOnlyList<AgentDefinition> GetAgents() =>
        _options.Agents.Concat(ReadMarkdownAgents()).GroupBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.Last()).ToList();

    public AgentDefinition GetRequiredAgent(string name) =>
        GetAgents().SingleOrDefault(agent => string.Equals(agent.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Agent '{name}' is not configured.");

    public AgentDefinition SaveAgent(DynamicAgentUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Agent name is required.", nameof(request));
        }

        var file = Path.Combine(paths.AgentsDirectory, $"{Sanitize(request.Name)}.md");
        var content = $"""
        ---
        name: {request.Name.Trim()}
        description: {request.Description.Trim()}
        model: {request.Model ?? string.Empty}
        allowed_tools: {string.Join(',', request.AllowedTools ?? [])}
        allowed_agents: {string.Join(',', request.AllowedAgents ?? [])}
        ---
        {request.SystemPrompt.Trim()}
        """;

        File.WriteAllText(file, content);

        return ParseMarkdownAgent(file) ?? throw new InvalidOperationException($"Could not save agent '{request.Name}'.");
    }

    private IReadOnlyList<AgentDefinition> ReadMarkdownAgents()
    {
        var directory = paths.AgentsDirectory;

        return Directory.EnumerateFiles(directory, "*.md")
            .Select(ParseMarkdownAgent)
            .OfType<AgentDefinition>()
            .ToList();
    }

    private static AgentDefinition? ParseMarkdownAgent(string file)
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

        var metadata = text[3..end]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        var prompt = text[(end + 5)..].Trim();

        if (!metadata.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        return new AgentDefinition(
            Name: name,
            Description: metadata.GetValueOrDefault("description") ?? $"Dynamic markdown agent {name}.",
            SystemPrompt: prompt,
            Model: EmptyToNull(metadata.GetValueOrDefault("model")),
            AllowedTools: SplitCsv(metadata.GetValueOrDefault("allowed_tools")),
            AllowedAgents: SplitCsv(metadata.GetValueOrDefault("allowed_agents")));
    }

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Sanitize(string value) =>
        string.Join("_", value.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
}

public sealed record DynamicAgentUpsertRequest(
    string Name,
    string Description,
    string SystemPrompt,
    string? Model = null,
    IReadOnlyList<string>? AllowedTools = null,
    IReadOnlyList<string>? AllowedAgents = null);
