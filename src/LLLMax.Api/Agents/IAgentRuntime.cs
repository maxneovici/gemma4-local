namespace LLLMax.Api.Agents;

public interface IAgentRuntime
{
    Task<AgentRunResponse> RunAsync(AgentRunRequest request, CancellationToken cancellationToken);
}
