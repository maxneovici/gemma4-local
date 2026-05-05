using System.Text;
using Gemma4Local.Api.Memory;
using Gemma4Local.Api.Models;
using Gemma4Local.Api.Options;
using Gemma4Local.Api.Services;
using Gemma4Local.Api.Tools;
using Microsoft.Extensions.Options;

namespace Gemma4Local.Api.Agents;

public sealed class AgentRuntime(
    IAgentRegistry agentRegistry,
    ILocalChatClient chatClient,
    ILocalToolRegistry toolRegistry,
    ILocalMemoryStore memoryStore,
    IOptions<LocalAiOptions> options) : IAgentRuntime
{
    private readonly LocalAiOptions _options = options.Value;

    public async Task<AgentRunResponse> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var agent = agentRegistry.GetRequiredAgent(request.Agent);
        var toolResults = new List<ToolExecutionResult>();
        var memoryContext = await BuildMemoryContextAsync(agent, request.Message, cancellationToken);
        var toolContext = BuildToolContext(agent, request.AllowTools);

        var toolCallExample = "{\"tool\":\"tool_name\",\"arguments\":{}}";
        var prompt = $"""
{agent.SystemPrompt}

Local-only constraints:
- Do not suggest cloud AI services unless the user explicitly asks for non-local alternatives.
- Prefer local tools and local memory.

Relevant local memory:
{memoryContext}

Available local tools:
{toolContext}

Tool protocol:
- If a tool is needed, respond with exactly one JSON object and no markdown: {toolCallExample}
- If no tool is needed, answer normally.
""";

        var firstPass = await chatClient.ChatAsync(new LocalChatRequest(
            Message: request.Message,
            Model: agent.Model,
            SystemPrompt: prompt), cancellationToken);

        if (request.AllowTools && ToolCallParser.TryParse(firstPass.Response, out var toolCall))
        {
            var tool = toolRegistry.GetRequiredTool(toolCall.Tool);
            EnsureToolAllowed(agent, tool.Name);

            var result = await tool.InvokeAsync(new LocalToolInvocation(
                ToolName: tool.Name,
                Arguments: toolCall.Arguments,
                Agent: agent,
                ConversationId: request.ConversationId), cancellationToken);

            toolResults.Add(new ToolExecutionResult(tool.Name, result.Content));

            var final = await chatClient.ChatAsync(new LocalChatRequest(
                Model: agent.Model,
                Messages:
                [
                    new LocalChatMessage("system", prompt),
                    new LocalChatMessage("user", request.Message),
                    new LocalChatMessage("assistant", firstPass.Response),
                    new LocalChatMessage("user", $"Tool {tool.Name} returned this result:\n{result.Content}"),
                    new LocalChatMessage("user", "Use the tool result to provide the final answer. Do not emit another tool call.")
                ]), cancellationToken);

            if (request.PersistToMemory)
            {
                await PersistInteractionAsync(agent, request, final.Response, cancellationToken);
            }

            return new AgentRunResponse(agent.Name, final.Response, toolResults);
        }

        if (request.PersistToMemory)
        {
            await PersistInteractionAsync(agent, request, firstPass.Response, cancellationToken);
        }

        return new AgentRunResponse(agent.Name, firstPass.Response, toolResults);
    }

    private async Task<string> BuildMemoryContextAsync(AgentDefinition agent, string message, CancellationToken cancellationToken)
    {
        if (!_options.Memory.Enabled)
        {
            return "Memory is disabled.";
        }

        var memories = await memoryStore.SearchAsync(new MemorySearchRequest(
            Collection: agent.Name,
            Query: message,
            Limit: _options.Memory.MaxContextItems), cancellationToken);

        if (memories.Count == 0)
        {
            return "No relevant memories found.";
        }

        var builder = new StringBuilder();

        foreach (var memory in memories)
        {
            builder.AppendLine($"- [{memory.Score:0.000}] {memory.Text}");
        }

        return builder.ToString();
    }

    private string BuildToolContext(AgentDefinition agent, bool allowTools)
    {
        if (!allowTools)
        {
            return "Tool use is disabled for this request.";
        }

        var tools = toolRegistry.GetTools()
            .Where(tool => agent.AllowedTools?.Contains(tool.Name, StringComparer.OrdinalIgnoreCase) == true)
            .Select(tool => $"- {tool.Name}: {tool.Description}. Arguments JSON schema: {tool.ArgumentsJsonSchema}");

        return string.Join(Environment.NewLine, tools);
    }

    private static void EnsureToolAllowed(AgentDefinition agent, string toolName)
    {
        if (agent.AllowedTools?.Contains(toolName, StringComparer.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException($"Agent '{agent.Name}' is not allowed to call tool '{toolName}'.");
        }
    }

    private async Task PersistInteractionAsync(AgentDefinition agent, AgentRunRequest request, string response, CancellationToken cancellationToken)
    {
        await memoryStore.UpsertAsync(new MemoryUpsertRequest(
            Collection: agent.Name,
            Text: $"User: {request.Message}{Environment.NewLine}{agent.Name}: {response}",
            Metadata: new Dictionary<string, string>
            {
                ["agent"] = agent.Name,
                ["conversationId"] = request.ConversationId ?? string.Empty
            }), cancellationToken);
    }
}
