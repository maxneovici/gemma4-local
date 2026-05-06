namespace LLLMax.Api.Mcp;

public interface IMcpRegistry
{
    Task<IReadOnlyList<McpServerRegistration>> ListAsync(CancellationToken cancellationToken);

    Task<McpServerRegistration?> GetAsync(string id, CancellationToken cancellationToken);

    Task<McpServerRegistration> RegisterAsync(McpRegisterServerRequest request, CancellationToken cancellationToken);

    Task<McpServerRegistration> ApproveToolAsync(string serverId, string toolName, CancellationToken cancellationToken);

    Task<McpServerRegistration> RejectToolAsync(string serverId, string toolName, CancellationToken cancellationToken);
}
