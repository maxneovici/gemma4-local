using LLLMax.Api.Agents;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace LLLMax.Api.Sessions;

public interface IToolUsePlanner
{
    Task<ToolUseDecision> DecideAsync(ToolUsePlanningRequest request, CancellationToken cancellationToken);
}

public sealed record ToolUsePlanningRequest(
    string Message,
    AgentDefinition Agent,
    bool ToolsAllowed,
    IReadOnlyList<LocalChatMessage> Messages);

public sealed record ToolUseDecision(
    string Policy,
    string Reason,
    IReadOnlyList<string> SuggestedTools,
    string Intent = "conversation",
    string ResponseMode = ResponseModes.Task,
    string? Model = null,
    string ReasoningEffort = "low",
    double Temperature = 0.4,
    double Confidence = 0)
{
    public bool ShouldRunTools => false;

    public bool ShouldUseOrchestrator => Policy.Equals(ToolUsePolicies.Orchestrate, StringComparison.OrdinalIgnoreCase);
}

public static class ToolUsePolicies
{
    public const string Quick = "quick";
    public const string Orchestrate = "orchestrate";
    public const string Clarify = "clarify";

    public const string Direct = Quick;
    public const string ToolRequired = Orchestrate;
    public const string BackgroundVerify = Orchestrate;
}

public static class ResponseModes
{
    public const string VoiceConversation = "voice_conversation";
    public const string Task = "task";
}

public sealed class ToolUsePlanner(ILocalChatClient chatClient, IOptions<LocalAiOptions> options) : IToolUsePlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalAiOptions _options = options.Value;

    public async Task<ToolUseDecision> DecideAsync(ToolUsePlanningRequest request, CancellationToken cancellationToken)
    {
        if (!_options.ModelRouter.UseAiPlanner)
        {
            return new ToolUseDecision(
                ToolUsePolicies.Clarify,
                "AI routing planner is disabled.",
                [],
                Confidence: 0);
        }

        if (!request.ToolsAllowed)
        {
            return Validate(request, new RouterDecisionDto(
                Intent: "conversation",
                ResponseMode: ResponseModes.VoiceConversation,
                ToolPolicy: ToolUsePolicies.Quick,
                Tools: [],
                Model: null,
                ReasoningEffort: "low",
                Temperature: 0.4,
                Confidence: 1,
                Reason: "Tools are disabled for this turn."));
        }

        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: RouterModel,
            Messages:
            [
                new LocalChatMessage("system", BuildRouterPrompt(request.Agent)),
                new LocalChatMessage("user", BuildRouterInput(request))
            ],
            EnableThinking: false,
            Temperature: 0,
            TopP: 0.8,
            TopK: 20,
            MaxOutputTokens: _options.ModelRouter.RouterMaxOutputTokens,
            KeepAlive: _options.ModelRouter.RouterKeepAlive), cancellationToken);

        if (!TryParseDecision(response.Response, out var decision))
        {
            return new ToolUseDecision(
                ToolUsePolicies.Clarify,
                $"Routing planner returned invalid JSON: {response.Response}",
                [],
                Confidence: 0);
        }

        return Validate(request, decision);
    }

    private string BuildRouterPrompt(AgentDefinition agent)
    {
        return $$"""
{{_options.ModelRouter.RouterSystemPrompt}}

Infer the user's real intent and choose execution parameters for a local assistant turn.

Do not answer the user. Return exactly one JSON object and no markdown.

Routing principles:
- Infer intent semantically from the user's request and conversation context. Do not use literal keyword matching.
- You are not allowed to answer factual questions, inspect memory, inspect files, claim something exists, or claim something is absent. Your only job is route selection.
- Choose quick for casual chat, greetings, opinions, and simple explanations that should stream immediately.
- Choose orchestrate when the main Gemma/coordinator should reason over the request and decide whether tools or subagents are needed.
- Choose clarify when intent is underspecified or required targets are missing.
- For voice-like casual turns, use responseMode "voice_conversation", low reasoning, and low temperature.
- For tool or implementation work, use responseMode "task" and the smallest reasoning effort likely to succeed.
- Return null for model unless the user explicitly selected a model. The runtime picks interactive vs deep response models from reasoning effort.
- If a request asks what is stored, persisted, remembered, indexed, available in local state, present in a vector store, present in a document, present on the web, or present behind an API, choose orchestrate. Do not choose specific tools or subagents.

Calibration examples:
- User asks: "What's up?" => quick, voice_conversation, no tools, low reasoning.
- User asks: "What can you find in persisted memory?" => orchestrate, task, low or medium reasoning.
- User asks: "Do you remember what we decided about model routing?" => orchestrate, task.
- User asks: "Is local vector memory storing anything about LLLMax?" => orchestrate, task.
- User asks: "Explain why local memory matters." => quick, voice_conversation, no tools unless the user asks to check stored memory.
- User asks: "Research the latest docs." => orchestrate, task.
- User asks: "Run the build." => orchestrate, task.

Valid JSON shape:
{
  "intent": "short_snake_case_intent",
  "responseMode": "voice_conversation|task",
  "toolPolicy": "quick|orchestrate|clarify",
  "tools": [],
  "model": null,
  "reasoningEffort": "low|medium|high",
  "temperature": 0.0,
  "confidence": 0.0,
  "reason": "brief routing reason"
}

Router/planner model: {{RouterModel}}

Tool and subagent selection rule:
- Always return an empty tools array.
- The coordinator, not the router, decides which tools or subagents to call after an orchestrate decision.

Agent:
- name: {{agent.Name}}
- description: {{agent.Description}}
""";
    }

    private string BuildRouterInput(ToolUsePlanningRequest request)
    {
        var recentMessages = Math.Max(0, request.Messages.Count - _options.ModelRouter.RouterMaxRecentMessages);
        var transcript = request.Messages.Count == 0
            ? "No prior messages."
            : string.Join("\n", request.Messages.Skip(recentMessages).Select(message => $"{message.Role}: {message.Content}"));

        return $$"""
Current user message:
{{request.Message}}

Recent conversation:
{{transcript}}
""";
    }

    private static bool TryParseDecision(string text, out RouterDecisionDto decision)
    {
        decision = default!;
        var json = ExtractJsonObject(text);

        if (json is null)
        {
            return false;
        }

        try
        {
            decision = JsonSerializer.Deserialize<RouterDecisionDto>(json, JsonOptions)!;
            return decision is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private ToolUseDecision Validate(ToolUsePlanningRequest request, RouterDecisionDto decision)
    {
        var tools = Array.Empty<string>();
        var policy = NormalizePolicy(decision.ToolPolicy, request.Message);
        var responseMode = decision.ResponseMode?.Equals(ResponseModes.VoiceConversation, StringComparison.OrdinalIgnoreCase) == true
            ? ResponseModes.VoiceConversation
            : ResponseModes.Task;

        return new ToolUseDecision(
            Policy: policy,
            Reason: string.IsNullOrWhiteSpace(decision.Reason) ? "AI router selected route." : decision.Reason.Trim(),
            SuggestedTools: tools,
            Intent: string.IsNullOrWhiteSpace(decision.Intent) ? "unknown" : decision.Intent.Trim(),
            ResponseMode: responseMode,
            Model: null,
            ReasoningEffort: NormalizeEffort(decision.ReasoningEffort),
            Temperature: Math.Clamp(decision.Temperature ?? 0.4, 0, 1),
            Confidence: Math.Clamp(decision.Confidence ?? 0, 0, 1));
    }

    private static string NormalizePolicy(string? policy, string message)
    {
        if (RequiresCoordinator(message))
        {
            return ToolUsePolicies.Orchestrate;
        }

        return policy?.Trim().ToLowerInvariant() switch
        {
            "tool_required" => ToolUsePolicies.Orchestrate,
            "background_verify" => ToolUsePolicies.Orchestrate,
            "direct" => ToolUsePolicies.Quick,
            ToolUsePolicies.Orchestrate => ToolUsePolicies.Orchestrate,
            ToolUsePolicies.Clarify => ToolUsePolicies.Clarify,
            _ => ToolUsePolicies.Quick
        };
    }

    private static bool RequiresCoordinator(string message)
    {
        var lower = message.ToLowerInvariant();
        return lower.Contains("run the build")
            || lower.Contains("run build")
            || lower.Contains("run tests")
            || lower.Contains("run the tests")
            || lower.Contains("check local")
            || lower.Contains("persisted memory")
            || lower.Contains("vector memory")
            || lower.Contains("on the web")
            || lower.Contains("research the latest")
            || lower.Contains("look up")
            || lower.Contains("what can you find")
            || lower.Contains("extract the invoice")
            || lower.Contains("vectorize")
            || lower.Contains("implement")
            || lower.Contains("fix ");
    }

    private static string NormalizeEffort(string? effort) =>
        effort?.Trim().ToLowerInvariant() switch
        {
            "medium" => "medium",
            "high" => "high",
            _ => "low"
        };

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private string RouterModel => _options.ModelRouter.RouterModel ?? _options.ModelRouter.InteractiveModel ?? _options.DefaultModel;

    private sealed record RouterDecisionDto(
        string? Intent,
        string? ResponseMode,
        string? ToolPolicy,
        IReadOnlyList<string>? Tools,
        string? Model,
        string? ReasoningEffort,
        double? Temperature,
        double? Confidence,
        string? Reason);
}

public sealed record RoutingEvaluationCase(string Name, string Message, string ExpectedPolicy);

public sealed record RoutingEvaluationResult(string Name, bool Passed, ToolUseDecision Decision, string ExpectedPolicy);

public sealed record RoutingEvaluationSummary(int Passed, int Total, IReadOnlyList<RoutingEvaluationResult> Results);

public interface IRoutingEvaluationService
{
    Task<ToolUseDecision> PlanAsync(string message, string? agent, bool allowTools, CancellationToken cancellationToken);

    Task<RoutingEvaluationSummary> EvaluateAsync(CancellationToken cancellationToken);
}

public sealed class RoutingEvaluationService(IToolUsePlanner planner, IAgentRegistry agents) : IRoutingEvaluationService
{
    private static readonly IReadOnlyList<RoutingEvaluationCase> Cases =
    [
        new("casual_voice", "What's up?", ToolUsePolicies.Direct),
        new("memory_lookup", "What can you find in the persisted memory subsystem?", ToolUsePolicies.ToolRequired),
        new("stored_memory_status", "Can you check local vector memory for what we stored about LLLMax?", ToolUsePolicies.ToolRequired),
        new("web_research", "Research the latest Ollama tool calling docs on the web.", ToolUsePolicies.ToolRequired),
        new("document_ocr", "Extract the invoice fields from uploaded document doc_123.", ToolUsePolicies.ToolRequired)
    ];

    public async Task<ToolUseDecision> PlanAsync(string message, string? agent, bool allowTools, CancellationToken cancellationToken)
    {
        var definition = agents.GetRequiredAgent(agent ?? "coordinator");
        return await planner.DecideAsync(new ToolUsePlanningRequest(message, definition, allowTools, []), cancellationToken);
    }

    public async Task<RoutingEvaluationSummary> EvaluateAsync(CancellationToken cancellationToken)
    {
        var agent = agents.GetRequiredAgent("coordinator");
        var results = new List<RoutingEvaluationResult>();

        foreach (var item in Cases)
        {
            var decision = await planner.DecideAsync(new ToolUsePlanningRequest(item.Message, agent, true, []), cancellationToken);
            var passed = decision.Policy.Equals(item.ExpectedPolicy, StringComparison.OrdinalIgnoreCase);

            results.Add(new RoutingEvaluationResult(item.Name, passed, decision, item.ExpectedPolicy));
        }

        return new RoutingEvaluationSummary(results.Count(result => result.Passed), results.Count, results);
    }
}
