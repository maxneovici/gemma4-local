using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LLLMax.Api.Agents;
using LLLMax.Api.BackgroundJobs;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using LLLMax.Api.Tasks;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Sessions;

public sealed class AssistantOrchestrator(
    IAssistantSessionStore sessions,
    IAgentRuntime agentRuntime,
    ILocalChatClient chatClient,
    ITaskGraphService taskGraphs,
    IAgentRegistry agentRegistry,
    IBackgroundJobService backgroundJobs,
    IRuntimeModelSettings runtimeModels,
    IOptions<LocalAiOptions> options) : IAssistantOrchestrator
{
    private readonly LocalAiOptions _options = options.Value;

    public async Task<SessionChatResponse> ChatAsync(string sessionId, SessionChatRequest request, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(sessionId, cancellationToken);
        await taskGraphs.RecordRunStartedAsync(sessionId, request.Message, cancellationToken);
        var userMessage = new LocalChatMessage("user", request.Message);
        var messages = session.Messages.Concat([userMessage]).ToList();
        var summarized = false;

        if (ShouldSummarize(messages))
        {
            var summary = await SummarizeAsync(session, messages, cancellationToken);
            var keep = _options.Orchestration.ContextSummaryKeepLastMessages;
            messages = [new LocalChatMessage("system", $"Conversation summary so far: {summary}"), .. messages.TakeLast(keep)];
            session = session with { Summary = summary };
            summarized = true;
        }

        var route = BuildRoute(request, session);
        Func<AgentRuntimeEvent, CancellationToken, Task> onEvent = async (runtimeEvent, token) =>
        {
            if (runtimeEvent.Tool is not null)
            {
                await taskGraphs.RecordToolProgressAsync(session.Id, new TaskGraphToolProgress(
                    Tool: runtimeEvent.Tool,
                    Status: RuntimeEventStatus(runtimeEvent),
                    Content: runtimeEvent.Content,
                    Arguments: runtimeEvent.Arguments,
                    Result: runtimeEvent.Result), token);
            }
        };
        var agentResponse = await agentRuntime.RunAsync(new AgentRunRequest(
            Agent: request.Agent ?? session.Agent,
            Message: request.Message,
            AllowTools: request.AllowTools,
            PersistToMemory: request.PersistToMemory,
            ConversationId: session.Id,
            Model: route.Model,
            ReasoningEffort: route.ReasoningEffort,
            Temperature: route.Temperature,
            Messages: messages,
            DelegationDepth: 0,
            OnEvent: onEvent), cancellationToken);

        var completedGraph = await taskGraphs.RecordRunCompletedAsync(session.Id, agentResponse.Response, agentResponse.ReasoningSteps ?? [], cancellationToken);
        var reasoningSteps = agentResponse.ReasoningSteps ?? [];
        var assistantMessage = new LocalChatMessage("assistant", agentResponse.Response, Guid.NewGuid().ToString("n"), reasoningSteps, taskGraphs.SnapshotCurrentTurn(completedGraph), agentResponse.ToolTraces, agentResponse.Citations);
        var nextMessages = messages.Concat([assistantMessage]).ToList();
        await sessions.SaveAsync(session with
        {
            Agent = request.Agent ?? session.Agent,
            Model = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages = nextMessages
        }, cancellationToken);
        await ScheduleMemoryConsolidationAsync(session.Id, request.PersistToMemory, cancellationToken);

        return new SessionChatResponse(
            SessionId: session.Id,
            Response: agentResponse.Response,
            Messages: nextMessages,
            Metrics: agentResponse.Metrics,
            ReasoningSteps: reasoningSteps,
            Summarized: summarized,
            Route: route);
    }

    public async IAsyncEnumerable<SessionChatStreamEvent> StreamChatAsync(
        string sessionId,
        SessionChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(sessionId, cancellationToken);
        var graph = await taskGraphs.RecordRunStartedAsync(sessionId, request.Message, cancellationToken);
        yield return new SessionChatStreamEvent("task_graph", Payload: graph);
        var route = BuildRoute(request, session);
        yield return new SessionChatStreamEvent("progress", $"Thinking with {route.Model}.", Payload: route);

        if (!request.AllowTools)
        {
            await foreach (var streamEvent in StreamDirectAsync(session, request, route, cancellationToken))
            {
                yield return streamEvent;
            }

            yield break;
        }

        var toolResponse = StreamToolRunAsync(session, request, route, cancellationToken);

        await foreach (var streamEvent in toolResponse.Events.WithCancellation(cancellationToken))
        {
            yield return streamEvent;
        }

        yield return new SessionChatStreamEvent("final", Result: await toolResponse.Result);
    }

    private ToolRunStream StreamToolRunAsync(
        AssistantSession session,
        SessionChatRequest request,
        SessionRoute route,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SessionChatStreamEvent>();
        var result = Task.Run(async () =>
        {
            try
            {
                var userMessage = new LocalChatMessage("user", request.Message);
                var messages = session.Messages.Concat([userMessage]).ToList();
                Func<AgentRuntimeEvent, CancellationToken, Task> onEvent = async (runtimeEvent, token) =>
                {
                    if (runtimeEvent.Tool is null)
                    {
                        return;
                    }

                    var graph = await taskGraphs.RecordToolProgressAsync(session.Id, new TaskGraphToolProgress(
                        Tool: runtimeEvent.Tool,
                        Status: RuntimeEventStatus(runtimeEvent),
                        Content: runtimeEvent.Content,
                        Arguments: runtimeEvent.Arguments,
                        Result: runtimeEvent.Result), token);

                    await channel.Writer.WriteAsync(new SessionChatStreamEvent("progress", runtimeEvent.Content, Payload: new { runtimeEvent, graph }), token);
                };
                var agentResponse = await agentRuntime.RunAsync(new AgentRunRequest(
                    Agent: request.Agent ?? session.Agent,
                    Message: request.Message,
                    AllowTools: request.AllowTools,
                    PersistToMemory: request.PersistToMemory,
                    ConversationId: session.Id,
                    Model: route.Model,
                    ReasoningEffort: route.ReasoningEffort,
                    Temperature: route.Temperature,
                    Messages: messages,
                    DelegationDepth: 0,
                    OnEvent: onEvent), cancellationToken);

                var graph = await taskGraphs.GetBySessionAsync(session.Id, cancellationToken);
                var reasoningSteps = agentResponse.ReasoningSteps ?? [];
                var nextMessages = messages.Concat([new LocalChatMessage("assistant", agentResponse.Response, Guid.NewGuid().ToString("n"), reasoningSteps, graph is null ? null : taskGraphs.SnapshotCurrentTurn(graph), agentResponse.ToolTraces, agentResponse.Citations)]).ToList();
                await sessions.SaveAsync(session with
                {
                    Agent = request.Agent ?? session.Agent,
                    Model = null,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Messages = nextMessages
                }, cancellationToken);
                await ScheduleMemoryConsolidationAsync(session.Id, request.PersistToMemory, cancellationToken);

                if (graph is not null)
                {
                    await channel.Writer.WriteAsync(new SessionChatStreamEvent("task_graph", Payload: graph), cancellationToken);
                }

                return new SessionChatResponse(
                    SessionId: session.Id,
                    Response: agentResponse.Response,
                    Messages: nextMessages,
                    Metrics: agentResponse.Metrics,
                    ReasoningSteps: reasoningSteps,
                    Summarized: false,
                    Route: route);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        return new ToolRunStream(channel.Reader.ReadAllAsync(cancellationToken), result);
    }

    private async IAsyncEnumerable<SessionChatStreamEvent> StreamDirectAsync(
        AssistantSession session,
        SessionChatRequest request,
        SessionRoute route,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userMessage = new LocalChatMessage("user", request.Message);
        var messages = session.Messages.Concat([userMessage]).ToList();
        var summarized = false;

        if (ShouldSummarize(messages))
        {
            var summary = await SummarizeAsync(session, messages, cancellationToken);
            var keep = _options.Orchestration.ContextSummaryKeepLastMessages;
            messages = [new LocalChatMessage("system", $"Conversation summary so far: {summary}"), .. messages.TakeLast(keep)];
            session = session with { Summary = summary };
            summarized = true;
        }

        var agent = agentRegistry.GetRequiredAgent(request.Agent ?? session.Agent);
        var streamMessages = BuildStreamMessages(agent, messages, route);
        var responseBuilder = new StringBuilder();
        LocalChatStreamChunk? finalChunk = null;

        await foreach (var chunk in chatClient.StreamChatAsync(new LocalChatRequest(
            Model: route.Model,
            Messages: streamMessages,
            EnableThinking: route.EnableThinking,
            Temperature: route.Temperature), cancellationToken))
        {
            finalChunk = chunk;

            if (!string.IsNullOrEmpty(chunk.Content))
            {
                responseBuilder.Append(chunk.Content);
                yield return new SessionChatStreamEvent("chunk", chunk.Content);
            }
        }

        var response = responseBuilder.ToString();
        var reasoningSteps = new[] { new ReasoningStep("stream", "Response streamed directly without tools.", DateTimeOffset.UtcNow) };
        var graph = await taskGraphs.RecordRunCompletedAsync(session.Id, response, reasoningSteps, cancellationToken);
        var nextMessages = messages.Concat([new LocalChatMessage("assistant", response, Guid.NewGuid().ToString("n"), reasoningSteps, taskGraphs.SnapshotCurrentTurn(graph), [], [])]).ToList();
        var metrics = new AgentRunMetrics(
            Model: finalChunk?.Model ?? route.Model,
            ReasoningEffort: route.ReasoningEffort,
            TotalDurationMs: finalChunk?.TotalDurationMs,
            PromptEvalCount: finalChunk?.PromptEvalCount,
            EvalCount: finalChunk?.EvalCount,
            TokensPerSecond: finalChunk?.TokensPerSecond,
            EstimatedContextTokens: ContextEstimator.EstimateTokens(streamMessages));
        var finalResponse = new SessionChatResponse(
            SessionId: session.Id,
            Response: response,
            Messages: nextMessages,
            Metrics: metrics,
            ReasoningSteps: reasoningSteps,
            Summarized: summarized,
            Route: route);

        await sessions.SaveAsync(session with
        {
            Agent = request.Agent ?? session.Agent,
            Model = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages = nextMessages
        }, cancellationToken);
        await ScheduleMemoryConsolidationAsync(session.Id, request.PersistToMemory, cancellationToken);
        yield return new SessionChatStreamEvent("task_graph", Payload: graph);
        yield return new SessionChatStreamEvent("final", Result: finalResponse);
    }

    private async IAsyncEnumerable<SessionChatStreamEvent> StreamFinalAnswerAsync(
        string sessionId,
        SessionChatRequest request,
        SessionChatResponse toolResponse,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (toolResponse.Messages.LastOrDefault()?.ToolTraces?.Any(trace => trace.Tool.Equals("smart_home", StringComparison.OrdinalIgnoreCase)) == true)
        {
            yield return new SessionChatStreamEvent("final", Result: toolResponse);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(toolResponse.Response))
        {
            yield return new SessionChatStreamEvent("final", Result: toolResponse);
            yield break;
        }

        var session = await sessions.GetAsync(sessionId, cancellationToken);
        var agent = agentRegistry.GetRequiredAgent(request.Agent ?? session.Agent);
        var route = BuildRoute(request, session);
        var responseBuilder = new StringBuilder();
        LocalChatStreamChunk? finalChunk = null;
        var messages = BuildStreamMessages(agent,
        [
            .. session.Messages.Take(Math.Max(0, session.Messages.Count - 1)),
            new LocalChatMessage("assistant", toolResponse.Response),
            new LocalChatMessage("user", "Stream the final user-facing answer now. Preserve the tool findings exactly, but do not mention internal protocol details.")
        ], null);

        yield return new SessionChatStreamEvent("progress", "Tool work complete. Streaming final answer...");

        await foreach (var chunk in chatClient.StreamChatAsync(new LocalChatRequest(
            Model: route.Model,
            Messages: messages,
            EnableThinking: route.EnableThinking,
            Temperature: route.Temperature), cancellationToken))
        {
            finalChunk = chunk;

            if (!string.IsNullOrEmpty(chunk.Content))
            {
                responseBuilder.Append(chunk.Content);
                yield return new SessionChatStreamEvent("chunk", chunk.Content);
            }
        }

        var finalText = responseBuilder.ToString();

        if (string.IsNullOrWhiteSpace(finalText))
        {
            yield return new SessionChatStreamEvent("final", Result: toolResponse);
            yield break;
        }

        var graph = await taskGraphs.RecordRunCompletedAsync(session.Id, finalText, toolResponse.ReasoningSteps, cancellationToken);
        var toolMessage = session.Messages.LastOrDefault();
        var rewrittenMessages = session.Messages.Take(session.Messages.Count - 1).Concat([new LocalChatMessage("assistant", finalText, Guid.NewGuid().ToString("n"), toolResponse.ReasoningSteps, taskGraphs.SnapshotCurrentTurn(graph), toolMessage?.ToolTraces, toolMessage?.Citations)]).ToList();
        await sessions.SaveAsync(session with { Messages = rewrittenMessages, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
        var metrics = new AgentRunMetrics(
            Model: finalChunk?.Model ?? route.Model,
            ReasoningEffort: route.ReasoningEffort,
            TotalDurationMs: finalChunk?.TotalDurationMs ?? toolResponse.Metrics?.TotalDurationMs,
            PromptEvalCount: finalChunk?.PromptEvalCount ?? toolResponse.Metrics?.PromptEvalCount,
            EvalCount: finalChunk?.EvalCount ?? toolResponse.Metrics?.EvalCount,
            TokensPerSecond: finalChunk?.TokensPerSecond ?? toolResponse.Metrics?.TokensPerSecond,
            EstimatedContextTokens: ContextEstimator.EstimateTokens(messages));
        var result = toolResponse with { Response = finalText, Messages = rewrittenMessages, Metrics = metrics };

        yield return new SessionChatStreamEvent("task_graph", Payload: graph);
        yield return new SessionChatStreamEvent("final", Result: result);
    }

    private bool ShouldSummarize(IReadOnlyList<LocalChatMessage> messages)
    {
        var triggerTokens = _options.Orchestration.ContextMaxEstimatedTokens * _options.Orchestration.ContextSummaryTriggerPercent / 100;
        return ContextEstimator.EstimateTokens(messages) >= triggerTokens;
    }

    private static string RuntimeEventStatus(AgentRuntimeEvent runtimeEvent) =>
        runtimeEvent.Kind switch
        {
            "tool_completed" => "complete",
            "tool_failed" => "blocked",
            _ => "active"
        };

    private async Task<string> SummarizeAsync(AssistantSession session, IReadOnlyList<LocalChatMessage> messages, CancellationToken cancellationToken)
    {
        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: runtimeModels.GetCoordinatorModel(),
            Messages:
            [
                new LocalChatMessage("system", "Summarize this conversation for future context. Preserve goals, decisions, constraints, tool findings, and unresolved tasks. Be concise."),
                new LocalChatMessage("user", string.Join("\n", messages.Select(message => $"{message.Role}: {message.Content}")))
            ],
            Temperature: 0.2), cancellationToken);

        return response.Response;
    }

    private SessionRoute BuildRoute(SessionChatRequest request, AssistantSession session)
    {
        var effort = NormalizeEffort(request.ReasoningEffort);
        var model = runtimeModels.GetCoordinatorModel();

        return new SessionRoute(model, effort, EnableThinking(effort), Temperature(effort));
    }

    private string NormalizeEffort(string? effort) =>
        effort?.Trim().ToLowerInvariant() switch
        {
            "medium" => "medium",
            "high" => "high",
            "low" => "low",
            _ => NormalizeConfiguredEffort(_options.Models.DefaultReasoningEffort)
        };

    private static string NormalizeConfiguredEffort(string? effort) =>
        effort?.Trim().ToLowerInvariant() switch
        {
            "medium" => "medium",
            "high" => "high",
            _ => "low"
        };

    private static bool EnableThinking(string effort) => effort is "medium" or "high";

    private static double Temperature(string effort) => effort switch
    {
        "medium" => 0.6,
        "high" => 0.7,
        _ => 0.4
    };

    private IReadOnlyList<LocalChatMessage> BuildStreamMessages(AgentDefinition agent, IReadOnlyList<LocalChatMessage> messages, SessionRoute? route)
    {
        var basePrompt = route?.ResponseMode.Equals(ResponseModes.VoiceConversation, StringComparison.OrdinalIgnoreCase) == true
            ? _options.ConversationSystemPrompt
            : _options.SystemPrompt;
        var systemPrompt = $"{basePrompt}\n\n{agent.SystemPrompt}\n\nTools are disabled for this streaming response. Answer directly.";

        return [new LocalChatMessage("system", systemPrompt), .. messages.Where(message => message.Role != "system")];
    }

    private sealed record ToolRunStream(IAsyncEnumerable<SessionChatStreamEvent> Events, Task<SessionChatResponse> Result);

    private async Task ScheduleMemoryConsolidationAsync(string sessionId, bool persistToMemory, CancellationToken cancellationToken)
    {
        if (!persistToMemory || !_options.Memory.Enabled)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToElement(new Memory.MemoryConsolidationRequest(sessionId, Collection: "core"));
        await backgroundJobs.EnqueueAsync(new BackgroundJobCreateRequest(
            Kind: BackgroundJobKinds.MemoryConsolidation,
            Payload: payload,
            Title: "Consolidate session memory",
            SessionId: sessionId,
            Agent: _options.Orchestration.DefaultAgent,
            NotifySession: false), cancellationToken);
    }
}
