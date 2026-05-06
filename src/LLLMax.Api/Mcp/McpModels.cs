using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace LLLMax.Api.Mcp;

public sealed record McpServerRegistration(
    string Id,
    string Name,
    string Transport,
    string Endpoint,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<McpToolRegistration> Tools);

public sealed record McpToolRegistration(
    string Name,
    string Description,
    JsonElement ArgumentsJsonSchema,
    string ApprovalStatus,
    DateTimeOffset UpdatedAt);

public sealed record McpRegisterServerRequest(
    [Required] string Name,
    [Required] string Transport,
    [Required] string Endpoint,
    IReadOnlyList<McpToolManifestRequest>? Tools = null);

public sealed record McpToolManifestRequest(
    [Required] string Name,
    string? Description,
    JsonElement? ArgumentsJsonSchema = null);

public sealed record McpToolInvocationRequest(string ServerId, string ToolName, JsonElement Arguments);

public sealed record McpToolInvocationApprovalPayload(string ServerId, string ServerName, string ToolName, JsonElement Arguments);
