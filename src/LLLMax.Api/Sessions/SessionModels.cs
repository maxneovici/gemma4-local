using LLLMax.Api.Agents;
using LLLMax.Api.Models;

namespace LLLMax.Api.Sessions;

public sealed record SessionCreateRequest(string? Title = null, string? Agent = null, string? Model = null);

public sealed record SessionChatRequest(
    string Message,
    string? Agent = null,
    string? Model = null,
    string? ReasoningEffort = null,
    bool AllowTools = true,
    bool PersistToMemory = true);

public sealed record SessionChatResponse(
    string SessionId,
    string Response,
    IReadOnlyList<LocalChatMessage> Messages,
    AgentRunMetrics? Metrics,
    IReadOnlyList<ReasoningStep> ReasoningSteps,
    bool Summarized);

public sealed record SessionChatStreamEvent(
    string Type,
    string? Content = null,
    SessionChatResponse? Result = null,
    object? Payload = null);

public sealed record AssistantSession(
    string Id,
    string Title,
    string Agent,
    string? Model,
    string? Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<LocalChatMessage> Messages);

public sealed record SessionListResponse(
    string Id,
    string Title,
    string Agent,
    string? Model,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string? Summary);
