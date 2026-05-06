using LLLMax.Api.Models;
using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Services;

public sealed class OllamaLocalChatClient(IOllamaApi ollamaApi, IOptions<LocalAiOptions> options) : ILocalChatClient
{
    private readonly LocalAiOptions _options = options.Value;

    public async Task<LocalChatResponse> ChatAsync(LocalChatRequest request, CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? _options.DefaultModel
            : request.Model;

        var messages = BuildMessages(request);
        var ollamaRequest = new OllamaChatRequest(
            Model: model,
            Stream: false,
            Messages: messages.Select(message => new OllamaMessage(message.Role, message.Content)).ToList(),
            Options: new OllamaOptions(
                Temperature: request.Temperature ?? _options.Sampling.Temperature,
                TopP: request.TopP ?? _options.Sampling.TopP,
                TopK: request.TopK ?? _options.Sampling.TopK),
            Think: request.EnableThinking ? true : null);

        var response = await ollamaApi.ChatAsync(ollamaRequest, cancellationToken);

        return new LocalChatResponse(
            Model: response.Model,
            Response: response.Message?.Content ?? string.Empty,
            TotalDurationMs: DurationToMilliseconds(response.TotalDuration),
            PromptEvalCount: response.PromptEvalCount,
            EvalCount: response.EvalCount,
            TokensPerSecond: TokensPerSecond(response.EvalCount, response.TotalDuration));
    }

    public async Task<IReadOnlyList<LocalModelResponse>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var models = await ollamaApi.GetModelsAsync(cancellationToken);

        return models
            .Select(model => new LocalModelResponse(
                Name: model.Name,
                SizeGb: Math.Round(model.Size / 1024d / 1024d / 1024d, 2),
                ModifiedAt: model.ModifiedAt))
            .ToList();
    }

    private IReadOnlyList<LocalChatMessage> BuildMessages(LocalChatRequest request)
    {
        if (request.Messages is { Count: > 0 })
        {
            return request.Messages;
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Either message or messages is required.", nameof(request));
        }

        var systemPrompt = request.SystemPrompt ?? _options.SystemPrompt;

        return
        [
            new LocalChatMessage("system", systemPrompt),
            new LocalChatMessage("user", request.Message)
        ];
    }

    private static double? DurationToMilliseconds(long? nanoseconds) =>
        nanoseconds is null ? null : Math.Round(nanoseconds.Value / 1_000_000d, 2);

    private static double? TokensPerSecond(int? tokens, long? nanoseconds) =>
        tokens is null || nanoseconds is null or <= 0
            ? null
            : Math.Round(tokens.Value / (nanoseconds.Value / 1_000_000_000d), 2);
}
