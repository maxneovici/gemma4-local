using System.Text.Json;

namespace LLLMax.Api.Mcp;

public interface IMcpBridge
{
    Task<string> InvokeAsync(McpServerRegistration server, McpToolRegistration tool, JsonElement arguments, CancellationToken cancellationToken);
}
