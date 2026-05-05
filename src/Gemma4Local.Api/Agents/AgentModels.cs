namespace Gemma4Local.Api.Agents;

public sealed record AgentDefinition(
    string Name,
    string Description,
    string SystemPrompt,
    string? Model = null,
    IReadOnlyList<string>? AllowedTools = null,
    IReadOnlyList<string>? AllowedAgents = null);

public sealed record AgentRunRequest(
    string Agent,
    string Message,
    bool AllowTools = true,
    bool PersistToMemory = false,
    string? ConversationId = null);

public sealed record AgentRunResponse(
    string Agent,
    string Response,
    IReadOnlyList<ToolExecutionResult> ToolResults);

public sealed record ToolExecutionResult(string Tool, string Result);

public sealed record AgentDelegationRequest(string Agent, string Message);

public sealed record AgentListResponse(string Name, string Description, string? Model, IReadOnlyList<string> AllowedTools, IReadOnlyList<string> AllowedAgents);
