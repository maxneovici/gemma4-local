using System.Text.Json;
using LLLMax.Api.Agents;

namespace LLLMax.Api.Tools;

public sealed record LocalToolInvocation(
    string ToolName,
    IReadOnlyDictionary<string, JsonElement> Arguments,
    AgentDefinition Agent,
    string? ConversationId,
    int DelegationDepth,
    Func<AgentRuntimeEvent, CancellationToken, Task>? OnEvent = null);

public sealed record LocalToolResult(string Content);
