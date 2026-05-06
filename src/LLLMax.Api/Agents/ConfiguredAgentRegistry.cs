using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Agents;

public sealed class ConfiguredAgentRegistry(IOptions<LocalAiOptions> options) : IAgentRegistry
{
    private readonly LocalAiOptions _options = options.Value;

    public IReadOnlyList<AgentDefinition> GetAgents() => _options.Agents.Count > 0 ? _options.Agents : DefaultAgents;

    public AgentDefinition GetRequiredAgent(string name) =>
        GetAgents().SingleOrDefault(agent => string.Equals(agent.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Agent '{name}' is not configured.");

    public AgentDefinition SaveAgent(DynamicAgentUpsertRequest request) =>
        throw new NotSupportedException("Dynamic markdown agents are not enabled for this registry.");

    private static readonly IReadOnlyList<AgentDefinition> DefaultAgents =
    [
        new AgentDefinition(
            Name: "coordinator",
            Description: "Routes work to specialist local agents and synthesizes the final answer.",
            SystemPrompt: "You are the coordinator agent. Break work into local subtasks when useful, delegate only to allowed local agents, and return concise final answers.",
            AllowedTools: ["delegate_to_agent", "memory_search", "memory_write"],
            AllowedAgents: ["researcher", "coder"]),
        new AgentDefinition(
            Name: "researcher",
            Description: "Explores local context and summarizes findings.",
            SystemPrompt: "You are a local research agent. Focus on concise factual findings from the supplied prompt and memory context.",
            AllowedTools: ["memory_search", "memory_write"],
            AllowedAgents: []),
        new AgentDefinition(
            Name: "coder",
            Description: "Produces implementation-oriented answers and code sketches.",
            SystemPrompt: "You are a local coding agent. Produce practical implementation guidance and identify tradeoffs.",
            AllowedTools: ["memory_search", "memory_write"],
            AllowedAgents: [])
    ];
}
