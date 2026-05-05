using System.Text.Json;
using Gemma4Local.Api.Agents;

namespace Gemma4Local.Api.Tools;

public sealed record LocalToolInvocation(
    string ToolName,
    IReadOnlyDictionary<string, JsonElement> Arguments,
    AgentDefinition Agent,
    string? ConversationId);

public sealed record LocalToolResult(string Content);
