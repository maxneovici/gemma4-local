using System.Text.Json;
using LLLMax.Api.Services;

namespace LLLMax.Api.Mcp;

public sealed class FileMcpRegistry(LocalDataPaths paths) : IMcpRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<McpServerRegistration>> ListAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.McpRegistryPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(paths.McpRegistryPath);
        return await JsonSerializer.DeserializeAsync<List<McpServerRegistration>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    public async Task<McpServerRegistration?> GetAsync(string id, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).SingleOrDefault(server => server.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task<McpServerRegistration> RegisterAsync(McpRegisterServerRequest request, CancellationToken cancellationToken)
    {
        ValidateLocalTransport(request.Transport, request.Endpoint);
        var now = DateTimeOffset.UtcNow;
        var server = new McpServerRegistration(
            Id: Guid.NewGuid().ToString("n"),
            Name: request.Name.Trim(),
            Transport: request.Transport.Trim().ToLowerInvariant(),
            Endpoint: request.Endpoint.Trim(),
            Status: "registered",
            CreatedAt: now,
            UpdatedAt: now,
            Tools: (request.Tools ?? []).Select(tool => new McpToolRegistration(
                Name: tool.Name.Trim(),
                Description: string.IsNullOrWhiteSpace(tool.Description) ? $"MCP tool {tool.Name.Trim()}" : tool.Description.Trim(),
                ArgumentsJsonSchema: tool.ArgumentsJsonSchema ?? DefaultSchema(),
                ApprovalStatus: "pending",
                UpdatedAt: now)).ToList());

        var servers = (await ListAsync(cancellationToken)).Where(existing => !existing.Name.Equals(server.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        servers.Add(server);
        await WriteAsync(servers, cancellationToken);

        return server;
    }

    public Task<McpServerRegistration> ApproveToolAsync(string serverId, string toolName, CancellationToken cancellationToken) =>
        SetToolStatusAsync(serverId, toolName, "approved", cancellationToken);

    public Task<McpServerRegistration> RejectToolAsync(string serverId, string toolName, CancellationToken cancellationToken) =>
        SetToolStatusAsync(serverId, toolName, "rejected", cancellationToken);

    private async Task<McpServerRegistration> SetToolStatusAsync(string serverId, string toolName, string status, CancellationToken cancellationToken)
    {
        var servers = (await ListAsync(cancellationToken)).ToList();
        var index = servers.FindIndex(server => server.Id.Equals(serverId, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            throw new InvalidOperationException($"MCP server '{serverId}' is not registered.");
        }

        var server = servers[index];
        var found = false;
        var tools = server.Tools.Select(tool =>
        {
            if (!tool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            {
                return tool;
            }

            found = true;
            return tool with { ApprovalStatus = status, UpdatedAt = DateTimeOffset.UtcNow };
        }).ToList();

        if (!found)
        {
            throw new InvalidOperationException($"MCP tool '{toolName}' is not registered for server '{server.Name}'.");
        }

        server = server with { Tools = tools, UpdatedAt = DateTimeOffset.UtcNow };
        servers[index] = server;
        await WriteAsync(servers, cancellationToken);

        return server;
    }

    private async Task WriteAsync(IReadOnlyList<McpServerRegistration> servers, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var stream = File.Create(paths.McpRegistryPath);
            await JsonSerializer.SerializeAsync(stream, servers, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateLocalTransport(string transport, string endpoint)
    {
        var normalized = transport.Trim().ToLowerInvariant();

        if (normalized is not ("http" or "sse" or "stdio"))
        {
            throw new ArgumentException("MCP transport must be http, sse, or stdio.");
        }

        if (normalized is "http" or "sse")
        {
            var uri = new Uri(endpoint, UriKind.Absolute);
            if (!uri.IsLoopback)
            {
                throw new ArgumentException("MCP HTTP/SSE endpoints must be loopback by default.");
            }
        }
    }

    private static JsonElement DefaultSchema() =>
        JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"additionalProperties\":true}");
}
