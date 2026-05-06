namespace LLLMax.Api.Agents;

public interface IAgentRegistry
{
    IReadOnlyList<AgentDefinition> GetAgents();

    AgentDefinition GetRequiredAgent(string name);

    AgentDefinition SaveAgent(DynamicAgentUpsertRequest request);
}
