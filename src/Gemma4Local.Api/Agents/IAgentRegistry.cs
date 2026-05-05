namespace Gemma4Local.Api.Agents;

public interface IAgentRegistry
{
    IReadOnlyList<AgentDefinition> GetAgents();

    AgentDefinition GetRequiredAgent(string name);
}
