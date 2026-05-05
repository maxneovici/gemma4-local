using Gemma4Local.Api.Models;
using Gemma4Local.Api.Options;
using Microsoft.Extensions.Options;

namespace Gemma4Local.Api.Services;

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
                TopK: request.TopK ?? _options.Sampling.TopK));

        var response = await ollamaApi.ChatAsync(ollamaRequest, cancellationToken);

        return new LocalChatResponse(
            Model: response.Model,
            Response: response.Message?.Content ?? string.Empty,
            TotalDurationMs: DurationToMilliseconds(response.TotalDuration),
            PromptEvalCount: response.PromptEvalCount,
            EvalCount: response.EvalCount);
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

        if (request.EnableThinking)
        {
            systemPrompt = $"<|think|>{systemPrompt}";
        }

        return
        [
            new LocalChatMessage("system", systemPrompt),
            new LocalChatMessage("user", request.Message)
        ];
    }

    private static double? DurationToMilliseconds(long? nanoseconds) =>
        nanoseconds is null ? null : Math.Round(nanoseconds.Value / 1_000_000d, 2);
}
