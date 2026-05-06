using System.Text.Json;

namespace LLLMax.Api.Mcp;

public sealed class McpBridge : IMcpBridge
{
    public Task<string> InvokeAsync(McpServerRegistration server, McpToolRegistration tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        // Execution is intentionally explicit and narrow for the first bridge step: approval and discovery
        // are durable now, while process/HTTP MCP transport execution can be enabled per transport next.
        return Task.FromResult($"MCP invocation approved but transport execution is not enabled yet. Server={server.Name}; Tool={tool.Name}; Arguments={arguments.GetRawText()}");
    }
}
