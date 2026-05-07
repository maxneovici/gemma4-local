using System.Text.Json;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.Mcp;

public sealed class EfMcpRegistry(
    IDbContextFactory<LocalDbContext> dbFactory,
    LocalDataPaths paths,
    ILogger<EfMcpRegistry> logger) : IMcpRegistry
{
    private const string ImportMarker = "mcp_registry_json_imported";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _imported;

    public async Task<IReadOnlyList<McpServerRegistration>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.McpServers
            .AsNoTracking()
            .Include(server => server.Tools)
            .OrderBy(server => server.Name)
            .Select(server => ToModel(server))
            .ToListAsync(cancellationToken);
    }

    public async Task<McpServerRegistration?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.McpServers
            .AsNoTracking()
            .Include(server => server.Tools)
            .SingleOrDefaultAsync(server => server.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<McpServerRegistration> RegisterAsync(McpRegisterServerRequest request, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
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

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.McpServers
                .Include(entity => entity.Tools)
                .SingleOrDefaultAsync(entity => entity.Name.ToLower() == server.Name.ToLower(), cancellationToken);

            if (existing is not null)
            {
                db.McpServers.Remove(existing);
            }

            db.McpServers.Add(ToEntity(server));
            await db.SaveChangesAsync(cancellationToken);
            return server;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<McpServerRegistration> ApproveToolAsync(string serverId, string toolName, CancellationToken cancellationToken) =>
        SetToolStatusAsync(serverId, toolName, "approved", cancellationToken);

    public Task<McpServerRegistration> RejectToolAsync(string serverId, string toolName, CancellationToken cancellationToken) =>
        SetToolStatusAsync(serverId, toolName, "rejected", cancellationToken);

    private async Task<McpServerRegistration> SetToolStatusAsync(string serverId, string toolName, string status, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var server = await db.McpServers
                .Include(entity => entity.Tools)
                .SingleOrDefaultAsync(entity => entity.Id == serverId, cancellationToken)
                ?? throw new InvalidOperationException($"MCP server '{serverId}' is not registered.");
            var tool = server.Tools.SingleOrDefault(tool => tool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"MCP tool '{toolName}' is not registered for server '{server.Name}'.");
            var now = DateTimeOffset.UtcNow;
            tool.ApprovalStatus = status;
            tool.UpdatedAt = now;
            server.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return ToModel(server);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureImportedAsync(CancellationToken cancellationToken) =>
        await JsonImport.ImportOnceAsync(dbFactory, _gate, () => _imported, () => _imported = true, ImportMarker, ImportJsonAsync, cancellationToken);

    private async Task ImportJsonAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.McpRegistryPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(paths.McpRegistryPath);
            var servers = await JsonSerializer.DeserializeAsync<List<McpServerRegistration>>(stream, JsonOptions, cancellationToken) ?? [];

            foreach (var server in servers)
            {
                if (await db.McpServers.AnyAsync(existing => existing.Id == server.Id || existing.Name.ToLower() == server.Name.ToLower(), cancellationToken))
                {
                    continue;
                }

                db.McpServers.Add(ToEntity(server));
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not import MCP registry file {File}", paths.McpRegistryPath);
        }
    }

    private static McpServerEntity ToEntity(McpServerRegistration server) =>
        new()
        {
            Id = server.Id,
            Name = server.Name,
            Transport = server.Transport,
            Endpoint = server.Endpoint,
            Status = server.Status,
            CreatedAt = server.CreatedAt,
            UpdatedAt = server.UpdatedAt,
            Tools = server.Tools.Select(tool => new McpToolEntity
            {
                ServerId = server.Id,
                Name = tool.Name,
                Description = tool.Description,
                ArgumentsJsonSchema = JsonElementValue.Serialize(tool.ArgumentsJsonSchema),
                ApprovalStatus = tool.ApprovalStatus,
                UpdatedAt = tool.UpdatedAt
            }).ToList()
        };

    private static McpServerRegistration ToModel(McpServerEntity entity) =>
        new(
            Id: entity.Id,
            Name: entity.Name,
            Transport: entity.Transport,
            Endpoint: entity.Endpoint,
            Status: entity.Status,
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt,
            Tools: entity.Tools
                .OrderBy(tool => tool.Name)
                .Select(tool => new McpToolRegistration(
                    Name: tool.Name,
                    Description: tool.Description,
                    ArgumentsJsonSchema: JsonElementValue.Parse(tool.ArgumentsJsonSchema),
                    ApprovalStatus: tool.ApprovalStatus,
                    UpdatedAt: tool.UpdatedAt))
                .ToList());

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
        JsonElementValue.Parse("{\"type\":\"object\",\"additionalProperties\":true}");
}
