using System.Text.Json;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Agents;

public sealed class OllamaNativeToolChatClient(IOllamaApi ollamaApi, IOptions<LocalAiOptions> options) : INativeToolChatClient
{
    private readonly LocalAiOptions _options = options.Value;

    public async Task<NativeToolChatResponse> ChatAsync(NativeToolChatRequest request, CancellationToken cancellationToken)
    {
        var response = await ollamaApi.ChatWithToolsAsync(new OllamaNativeToolChatRequest(
            Model: request.Model,
            Stream: false,
            Messages: request.Messages.Select(message => new OllamaNativeMessage(message.Role, message.Content)).ToList(),
            Options: new OllamaOptions(request.Temperature, _options.Sampling.TopP, _options.Sampling.TopK),
            Tools: request.Tools.Select(tool => tool.ToOllamaToolDefinition()).ToList(),
            Think: request.EnableThinking ? true : null), cancellationToken);

        var toolCalls = response.Message?.ToolCalls?
            .Select(call => new ParsedToolCall(
                Tool: call.Function.Name,
                Arguments: JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.Function.Arguments.ToJsonString()) ?? []))
            .ToList() ?? [];

        return new NativeToolChatResponse(
            Model: response.Model,
            Content: response.Message?.Content ?? string.Empty,
            ToolCalls: toolCalls,
            Metrics: new AgentRunMetrics(
                Model: response.Model,
                ReasoningEffort: string.Empty,
                TotalDurationMs: DurationToMilliseconds(response.TotalDuration),
                PromptEvalCount: response.PromptEvalCount,
                EvalCount: response.EvalCount,
                TokensPerSecond: TokensPerSecond(response.EvalCount, response.TotalDuration),
                EstimatedContextTokens: 0));
    }

    private static double? DurationToMilliseconds(long? nanoseconds) =>
        nanoseconds is null ? null : Math.Round(nanoseconds.Value / 1_000_000d, 2);

    private static double? TokensPerSecond(int? tokens, long? nanoseconds) =>
        tokens is null || nanoseconds is null or <= 0
            ? null
            : Math.Round(tokens.Value / (nanoseconds.Value / 1_000_000_000d), 2);
}
