using LLLMax.Api.Approvals;
using LLLMax.Api.Mcp;

namespace LLLMax.Api.Tools;

public sealed class LocalToolRegistry(IEnumerable<ILocalTool> tools, IMcpRegistry mcpRegistry, IApprovalService approvals, IMcpBridge mcpBridge) : ILocalToolRegistry
{
    private readonly IReadOnlyList<ILocalTool> _builtInTools = tools.ToList();

    public IReadOnlyList<ILocalTool> GetTools() =>
        [.. _builtInTools, .. GetApprovedMcpTools()];

    public ILocalTool GetRequiredTool(string name) =>
        GetTools().SingleOrDefault(tool => string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Tool '{name}' is not registered.");

    private IReadOnlyList<ILocalTool> GetApprovedMcpTools() =>
        mcpRegistry.ListAsync(CancellationToken.None).GetAwaiter().GetResult()
            .SelectMany(server => server.Tools
                .Where(tool => tool.ApprovalStatus.Equals("approved", StringComparison.OrdinalIgnoreCase))
                .Select(tool => new McpLocalTool(server, tool, approvals, mcpBridge)))
            .ToList();
}
