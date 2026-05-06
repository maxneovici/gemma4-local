using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Agents;

namespace LLLMax.Api.Tools;

public sealed class AgentDelegationTool(IServiceProvider serviceProvider) : LocalToolBase<AgentDelegationArguments>
{
    public override string Name => "delegate_to_agent";

    public override string Description => "Ask another configured local agent to handle a subtask.";

    protected override async Task<LocalToolResult> InvokeAsync(AgentDelegationArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.Agent.AllowedAgents?.Contains(arguments.Agent, StringComparer.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException($"Agent '{invocation.Agent.Name}' is not allowed to delegate to '{arguments.Agent}'.");
        }

        var agentRuntime = serviceProvider.GetRequiredService<IAgentRuntime>();
        var response = await agentRuntime.RunAsync(new AgentRunRequest(
            Agent: arguments.Agent,
            Message: arguments.Message,
            AllowTools: false,
            ConversationId: invocation.ConversationId), cancellationToken);

        return new LocalToolResult(response.Response);
    }
}

public sealed record AgentDelegationArguments([property: Required] string Agent, [property: Required] string Message);
