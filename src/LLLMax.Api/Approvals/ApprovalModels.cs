using System.Text.Json;

namespace LLLMax.Api.Approvals;

public sealed record ApprovalRequest(
    string Id,
    string Kind,
    string Status,
    string Title,
    string Description,
    JsonElement Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ConversationId = null,
    string? DecisionReason = null,
    string? Scope = null);

public sealed record ApprovalCreateRequest(
    string Kind,
    string Title,
    string Description,
    JsonElement Payload,
    string? ConversationId = null);

public sealed record ApprovalDecisionRequest(string? Reason = null, string? Scope = null);

public static class ApprovalScopes
{
    public const string Once = "once";
    public const string Session = "session";
    public const string Persistent = "persistent";
}
