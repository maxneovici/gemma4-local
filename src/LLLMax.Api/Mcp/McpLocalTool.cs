using System.Text.Json;
using System.Text.Json.Nodes;
using LLLMax.Api.Approvals;
using LLLMax.Api.Models;
using LLLMax.Api.Tools;

namespace LLLMax.Api.Mcp;

public sealed class McpLocalTool(
    McpServerRegistration server,
    McpToolRegistration tool,
    IApprovalService approvals,
    IMcpBridge bridge) : ILocalTool
{
    public string Name => $"mcp_{Sanitize(server.Name)}_{Sanitize(tool.Name)}";

    public string Description => $"MCP tool from {server.Name}: {tool.Description}. Requires per-invocation human approval.";

    public string ArgumentsJsonSchema => tool.ArgumentsJsonSchema.GetRawText();

    public OllamaToolDefinition ToOllamaToolDefinition() => new(
        Type: "function",
        Function: new OllamaToolFunction(
            Name: Name,
            Description: Description,
            Parameters: JsonNode.Parse(ArgumentsJsonSchema) ?? new JsonObject()));

    public async Task<LocalToolResult> InvokeAsync(LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(invocation.Arguments));
        var payload = JsonSerializer.SerializeToElement(new McpToolInvocationApprovalPayload(server.Id, server.Name, tool.Name, arguments));
        var existingApproval = await FindReusableApprovalAsync(payload, cancellationToken);

        if (existingApproval is null)
        {
            var approval = await approvals.CreateAsync(new ApprovalCreateRequest(
                Kind: "mcp_tool_invocation",
                Title: $"Run MCP tool {server.Name}/{tool.Name}",
                Description: $"Agent '{invocation.Agent.Name}' requested MCP tool '{tool.Name}'. Approve to execute this exact invocation.",
                Payload: payload,
                ConversationId: invocation.ConversationId), cancellationToken);

            return new LocalToolResult($"MCP tool invocation requires approval. ApprovalId={approval.Id}. Ask the user to approve or reject it in the approval queue, then retry if approved.");
        }

        if (existingApproval.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalToolResult($"MCP tool invocation was rejected. ApprovalId={existingApproval.Id}.");
        }

        return new LocalToolResult(await bridge.InvokeAsync(server, tool, arguments, cancellationToken));
    }

    private async Task<ApprovalRequest?> FindReusableApprovalAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var approvalsList = await approvals.ListAsync(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(payload);

        return approvalsList.FirstOrDefault(approval =>
            approval.Kind.Equals("mcp_tool_invocation", StringComparison.OrdinalIgnoreCase)
            && JsonSerializer.Serialize(approval.Payload) == payloadJson
            && approval.Status is "approved" or "rejected");
    }

    private static string Sanitize(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
}
