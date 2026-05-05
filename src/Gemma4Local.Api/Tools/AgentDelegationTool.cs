using System.Text.Json;
using Gemma4Local.Api.Agents;

namespace Gemma4Local.Api.Tools;

public sealed class AgentDelegationTool(IServiceProvider serviceProvider) : ILocalTool
{
    public string Name => "delegate_to_agent";

    public string Description => "Ask another configured local agent to handle a subtask.";

    public string ArgumentsJsonSchema => "{\"type\":\"object\",\"properties\":{\"agent\":{\"type\":\"string\"},\"message\":{\"type\":\"string\"}},\"required\":[\"agent\",\"message\"]}";

    public async Task<LocalToolResult> InvokeAsync(LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var agent = GetRequiredString(invocation.Arguments, "agent");
        var message = GetRequiredString(invocation.Arguments, "message");

        if (invocation.Agent.AllowedAgents?.Contains(agent, StringComparer.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException($"Agent '{invocation.Agent.Name}' is not allowed to delegate to '{agent}'.");
        }

        var agentRuntime = serviceProvider.GetRequiredService<IAgentRuntime>();
        var response = await agentRuntime.RunAsync(new AgentRunRequest(
            Agent: agent,
            Message: message,
            AllowTools: false,
            ConversationId: invocation.ConversationId), cancellationToken);

        return new LocalToolResult(response.Response);
    }

    private static string GetRequiredString(IReadOnlyDictionary<string, JsonElement> arguments, string name)
    {
        if (arguments.TryGetValue(name, out var value) && value.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        throw new ArgumentException($"Tool argument '{name}' is required.");
    }
}
