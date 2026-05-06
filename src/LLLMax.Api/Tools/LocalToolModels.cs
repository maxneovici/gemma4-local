using System.Text.Json;
using LLLMax.Api.Agents;

namespace LLLMax.Api.Tools;

public sealed record LocalToolInvocation(
    string ToolName,
    IReadOnlyDictionary<string, JsonElement> Arguments,
    AgentDefinition Agent,
    string? ConversationId,
    int DelegationDepth);

public sealed record LocalToolResult(string Content);
