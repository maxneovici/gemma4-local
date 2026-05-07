using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using LLLMax.Api.Agents;
using LLLMax.Api.Tasks;

namespace LLLMax.Api.Models;

public sealed record LocalChatRequest(
    string? Message = null,
    IReadOnlyList<LocalChatMessage>? Messages = null,
    string? Model = null,
    string? SystemPrompt = null,
    bool EnableThinking = false,
    double? Temperature = null,
    double? TopP = null,
    int? TopK = null);

public sealed record LocalChatMessage(
    string Role,
    string Content,
    string? TraceId = null,
    IReadOnlyList<ReasoningStep>? ReasoningSteps = null,
    TaskGraph? TaskGraph = null,
    IReadOnlyList<ToolTraceEntry>? ToolTraces = null,
    IReadOnlyList<CitationSource>? Citations = null);

public sealed record LocalChatResponse(
    string Model,
    string Response,
    double? TotalDurationMs,
    int? PromptEvalCount,
    int? EvalCount,
    double? TokensPerSecond = null);

public sealed record LocalChatStreamChunk(
    string Model,
    string Content,
    bool Done,
    double? TotalDurationMs = null,
    int? PromptEvalCount = null,
    int? EvalCount = null,
    double? TokensPerSecond = null);

public sealed record HealthResponse(bool IsHealthy, string Message);

public sealed record ErrorResponse(string Error);

public sealed record LocalModelResponse(string Name, double SizeGb, DateTimeOffset ModifiedAt);

public sealed record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("messages")] IReadOnlyList<OllamaMessage> Messages,
    [property: JsonPropertyName("options")] OllamaOptions Options,
    [property: JsonPropertyName("think")] bool? Think = null,
    [property: JsonPropertyName("tools")] IReadOnlyList<OllamaToolDefinition>? Tools = null);

public sealed record OllamaStreamChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("messages")] IReadOnlyList<OllamaMessage> Messages,
    [property: JsonPropertyName("options")] OllamaOptions Options,
    [property: JsonPropertyName("think")] bool? Think = null);

public sealed record OllamaNativeToolChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("messages")] IReadOnlyList<OllamaNativeMessage> Messages,
    [property: JsonPropertyName("options")] OllamaOptions Options,
    [property: JsonPropertyName("tools")] IReadOnlyList<OllamaToolDefinition> Tools,
    [property: JsonPropertyName("think")] bool? Think = null);

public sealed record OllamaPullRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stream")] bool Stream);

public sealed record OllamaMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record OllamaNativeMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OllamaToolCall>? ToolCalls = null);

public sealed record OllamaToolDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OllamaToolFunction Function);

public sealed record OllamaToolFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonNode Parameters);

public sealed record OllamaToolCall(
    [property: JsonPropertyName("function")] OllamaToolCallFunction Function);

public sealed record OllamaToolCallFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonObject Arguments);

public sealed record OllamaVisionMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("images")] IReadOnlyList<string>? Images = null);

public sealed record OllamaVisionChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("messages")] IReadOnlyList<OllamaVisionMessage> Messages,
    [property: JsonPropertyName("options")] OllamaOptions Options,
    [property: JsonPropertyName("think")] bool? Think = null);

public sealed record OllamaOptions(
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("top_p")] double TopP,
    [property: JsonPropertyName("top_k")] int TopK);

public sealed record OllamaChatResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("message")] OllamaNativeMessage? Message,
    [property: JsonPropertyName("total_duration")] long? TotalDuration,
    [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
    [property: JsonPropertyName("eval_count")] int? EvalCount);

public sealed record OllamaChatStreamResponse(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("message")] OllamaNativeMessage? Message,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("total_duration")] long? TotalDuration,
    [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
    [property: JsonPropertyName("eval_count")] int? EvalCount);

public sealed record OllamaModelsResponse(
    [property: JsonPropertyName("models")] IReadOnlyList<OllamaModel> Models);

public sealed record OllamaModel(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("modified_at")] DateTimeOffset ModifiedAt,
    [property: JsonPropertyName("size")] long Size);

public sealed record OpenAiModelsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<OllamaModelInfo> Data);

public sealed record OllamaModelInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy);
