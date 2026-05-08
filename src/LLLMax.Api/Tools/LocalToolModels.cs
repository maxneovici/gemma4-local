using System.Text.Json;
using LLLMax.Api.Agents;
using LLLMax.Api.Models;

namespace LLLMax.Api.Tools;

public sealed record LocalToolInvocation(
    string ToolName,
    IReadOnlyDictionary<string, JsonElement> Arguments,
    AgentDefinition Agent,
    string? ConversationId,
    int DelegationDepth,
    IReadOnlyList<LocalChatMessage>? Messages = null,
    Func<AgentRuntimeEvent, CancellationToken, Task>? OnEvent = null);

public sealed record LocalToolResult(
    string Content,
    IReadOnlyList<CitationSource>? Citations = null);
