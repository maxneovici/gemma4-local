using LLLMax.Api.Models;

namespace LLLMax.Api.Agents;

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
    string? ConversationId = null,
    string? Model = null,
    string? ReasoningEffort = null,
    int? MaxToolIterations = null,
    IReadOnlyList<LocalChatMessage>? Messages = null,
    int DelegationDepth = 0,
    Func<AgentRuntimeEvent, CancellationToken, Task>? OnEvent = null);

public sealed record AgentRunResponse(
    string Agent,
    string Response,
    IReadOnlyList<ToolExecutionResult> ToolResults,
    AgentRunMetrics? Metrics = null,
    IReadOnlyList<ReasoningStep>? ReasoningSteps = null,
    bool TaskComplete = true,
    string? SessionId = null);

public sealed record ToolExecutionResult(string Tool, string Result);

public sealed record ReasoningStep(string Kind, string Content, DateTimeOffset CreatedAt);

public sealed record AgentRunMetrics(
    string Model,
    string ReasoningEffort,
    double? TotalDurationMs,
    int? PromptEvalCount,
    int? EvalCount,
    double? TokensPerSecond,
    int EstimatedContextTokens);

public sealed record AgentRuntimeEvent(
    string Kind,
    string Content,
    string? Tool = null,
    IReadOnlyDictionary<string, string>? Arguments = null,
    string? Result = null);

public sealed record AgentDelegationRequest(string Agent, string Message);

public sealed record AgentListResponse(string Name, string Description, string? Model, IReadOnlyList<string> AllowedTools, IReadOnlyList<string> AllowedAgents);
