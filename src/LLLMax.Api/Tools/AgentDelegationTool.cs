using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Agents;
using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Tools;

public sealed class AgentDelegationTool(IServiceProvider serviceProvider, IOptions<LocalAiOptions> options) : LocalToolBase<AgentDelegationArguments>
{
    private readonly LocalAiOptions _options = options.Value;

    public override string Name => "delegate_to_agent";

    public override string Description => "Ask another configured local agent to handle a subtask.";

    protected override async Task<LocalToolResult> InvokeAsync(AgentDelegationArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.DelegationDepth >= _options.Orchestration.MaxDelegationDepth)
        {
            return new LocalToolResult($"Delegation blocked: max depth {_options.Orchestration.MaxDelegationDepth} reached.");
        }

        if (invocation.Agent.AllowedAgents?.Contains(arguments.Agent, StringComparer.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException($"Agent '{invocation.Agent.Name}' is not allowed to delegate to '{arguments.Agent}'.");
        }

        var agentRuntime = serviceProvider.GetRequiredService<IAgentRuntime>();
        await PublishAsync(invocation, new AgentRuntimeEvent(
            Kind: "tool_started",
            Content: $"Delegating to {arguments.Agent}: {arguments.Message}",
            Tool: $"delegate_to_agent/{arguments.Agent}"), cancellationToken);

        var response = await agentRuntime.RunAsync(new AgentRunRequest(
            Agent: arguments.Agent,
            Message: arguments.Message,
            AllowTools: arguments.AllowTools ?? true,
            ConversationId: invocation.ConversationId,
            DelegationDepth: invocation.DelegationDepth + 1,
            OnEvent: invocation.OnEvent is null
                ? null
                : async (runtimeEvent, token) => await invocation.OnEvent(runtimeEvent with
                {
                    Tool = runtimeEvent.Tool is null ? null : $"{arguments.Agent}/{runtimeEvent.Tool}",
                    Content = runtimeEvent.Kind.Equals("model_delta", StringComparison.OrdinalIgnoreCase)
                        ? runtimeEvent.Content
                        : $"[{arguments.Agent}] {runtimeEvent.Content}"
                }, token)), cancellationToken);

        await PublishAsync(invocation, new AgentRuntimeEvent(
            Kind: "tool_completed",
            Content: $"{arguments.Agent} completed delegated work.",
            Tool: $"delegate_to_agent/{arguments.Agent}",
            Result: response.Response), cancellationToken);

        return new LocalToolResult(response.Response);
    }

    private static Task PublishAsync(LocalToolInvocation invocation, AgentRuntimeEvent runtimeEvent, CancellationToken cancellationToken) =>
        invocation.OnEvent?.Invoke(runtimeEvent, cancellationToken) ?? Task.CompletedTask;
}

public sealed record AgentDelegationArguments([property: Required] string Agent, [property: Required] string Message, bool? AllowTools = true);
