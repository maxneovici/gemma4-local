using LLLMax.Api.Options;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Agents;

public sealed class MarkdownAgentRegistry(IOptions<LocalAiOptions> options, LocalDataPaths paths, IDbContextFactory<LocalDbContext> dbFactory) : IAgentRegistry
{
    private const string PromptOverridePrefix = "agent_prompt_override:";
    private readonly LocalAiOptions _options = options.Value;

    public IReadOnlyList<AgentDefinition> GetAgents() =>
        ApplyPromptOverrides(_options.Agents.Concat(ReadMarkdownAgents()).GroupBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.Last()).ToList());

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

    public AgentPromptResponse GetPrompt(string name)
    {
        var agent = GetBaseAgents().SingleOrDefault(agent => string.Equals(agent.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Agent '{name}' is not configured.");
        var overridePrompt = GetPromptOverride(agent.Name);

        return new AgentPromptResponse(agent.Name, agent.SystemPrompt, overridePrompt, overridePrompt ?? agent.SystemPrompt, overridePrompt is not null);
    }

    public AgentPromptResponse SavePromptOverride(string name, AgentPromptOverrideRequest request)
    {
        var agent = GetBaseAgents().SingleOrDefault(agent => string.Equals(agent.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Agent '{name}' is not configured.");
        var prompt = request.SystemPrompt?.Trim();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("System prompt is required.", nameof(request));
        }

        using var db = dbFactory.CreateDbContext();
        var key = PromptOverrideKey(agent.Name);
        var existing = db.AppMetadata.Find(key);

        if (existing is null)
        {
            db.AppMetadata.Add(new AppMetadataEntity { Key = key, Value = prompt });
        }
        else
        {
            existing.Value = prompt;
        }

        db.SaveChanges();

        return new AgentPromptResponse(agent.Name, agent.SystemPrompt, prompt, prompt, true);
    }

    public AgentPromptResponse ResetPromptOverride(string name)
    {
        var agent = GetBaseAgents().SingleOrDefault(agent => string.Equals(agent.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Agent '{name}' is not configured.");

        using var db = dbFactory.CreateDbContext();
        var existing = db.AppMetadata.Find(PromptOverrideKey(agent.Name));

        if (existing is not null)
        {
            db.AppMetadata.Remove(existing);
            db.SaveChanges();
        }

        return new AgentPromptResponse(agent.Name, agent.SystemPrompt, null, agent.SystemPrompt, false);
    }

    private IReadOnlyList<AgentDefinition> GetBaseAgents() =>
        _options.Agents.Concat(ReadMarkdownAgents()).GroupBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.Last()).ToList();

    private IReadOnlyList<AgentDefinition> ApplyPromptOverrides(IReadOnlyList<AgentDefinition> agents)
    {
        var overrides = GetPromptOverrides();
        return agents.Select(agent => overrides.TryGetValue(agent.Name, out var prompt)
            ? agent with { SystemPrompt = prompt }
            : agent).ToList();
    }

    private IReadOnlyDictionary<string, string> GetPromptOverrides()
    {
        using var db = dbFactory.CreateDbContext();
        return db.AppMetadata
            .Where(item => item.Key.StartsWith(PromptOverridePrefix))
            .ToDictionary(item => item.Key[PromptOverridePrefix.Length..], item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    private string? GetPromptOverride(string name)
    {
        using var db = dbFactory.CreateDbContext();
        return db.AppMetadata.Find(PromptOverrideKey(name))?.Value;
    }

    private static string PromptOverrideKey(string name) => $"{PromptOverridePrefix}{name}";

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

public sealed record AgentPromptOverrideRequest(string SystemPrompt);

public sealed record AgentPromptResponse(string Name, string BaseSystemPrompt, string? OverrideSystemPrompt, string EffectiveSystemPrompt, bool HasOverride);
