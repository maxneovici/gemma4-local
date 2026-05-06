using LLLMax.Api.Models;
using LLLMax.Api.Tools;

namespace LLLMax.Api.Agents;

public interface INativeToolChatClient
{
    Task<NativeToolChatResponse> ChatAsync(NativeToolChatRequest request, CancellationToken cancellationToken);
}

public sealed record NativeToolChatRequest(
    string Model,
    IReadOnlyList<LocalChatMessage> Messages,
    IReadOnlyList<ILocalTool> Tools,
    bool EnableThinking,
    double Temperature);

public sealed record NativeToolChatResponse(
    string Model,
    string Content,
    IReadOnlyList<ParsedToolCall> ToolCalls,
    AgentRunMetrics Metrics);
