using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using LLLMax.Api.Agents;
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
    IModelRouter modelRouter,
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

        var agentResponse = await agentRuntime.RunAsync(new AgentRunRequest(
            Agent: request.Agent ?? session.Agent,
            Message: request.Message,
            AllowTools: request.AllowTools,
            PersistToMemory: request.PersistToMemory,
            ConversationId: session.Id,
            Model: request.Model ?? session.Model,
            ReasoningEffort: request.ReasoningEffort,
            Messages: messages,
            DelegationDepth: 0,
            OnEvent: async (runtimeEvent, token) =>
            {
                if (runtimeEvent.Tool is not null)
                {
                    await taskGraphs.RecordToolProgressAsync(session.Id, new TaskGraphToolProgress(
                        Tool: runtimeEvent.Tool,
                        Status: runtimeEvent.Kind == "tool_completed" ? "complete" : "active",
                        Content: runtimeEvent.Content,
                        Arguments: runtimeEvent.Arguments,
                        Result: runtimeEvent.Result), token);
                }
            }), cancellationToken);

        var nextMessages = messages.Concat([new LocalChatMessage("assistant", agentResponse.Response)]).ToList();
        await sessions.SaveAsync(session with
        {
            Agent = request.Agent ?? session.Agent,
            Model = request.Model ?? session.Model,
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages = nextMessages
        }, cancellationToken);
        await taskGraphs.RecordRunCompletedAsync(session.Id, agentResponse.Response, agentResponse.ReasoningSteps ?? [], cancellationToken);

        return new SessionChatResponse(
            SessionId: session.Id,
            Response: agentResponse.Response,
            Messages: nextMessages,
            Metrics: agentResponse.Metrics,
            ReasoningSteps: agentResponse.ReasoningSteps ?? [],
            Summarized: summarized);
    }

    public async IAsyncEnumerable<SessionChatStreamEvent> StreamChatAsync(
        string sessionId,
        SessionChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(sessionId, cancellationToken);
        var graph = await taskGraphs.RecordRunStartedAsync(sessionId, request.Message, cancellationToken);
        yield return new SessionChatStreamEvent("task_graph", Payload: graph);

        if (request.AllowTools)
        {
            var toolResponse = StreamToolRunAsync(session, request, cancellationToken);

            await foreach (var streamEvent in toolResponse.Events.WithCancellation(cancellationToken))
            {
                yield return streamEvent;
            }

            var postTool = StreamFinalAnswerAsync(sessionId, request, await toolResponse.Result, cancellationToken);

            await foreach (var streamEvent in postTool)
            {
                yield return streamEvent;
            }

            yield break;
        }

        await foreach (var streamEvent in StreamDirectAsync(session, request, cancellationToken))
        {
            yield return streamEvent;
        }
    }

    private ToolRunStream StreamToolRunAsync(
        AssistantSession session,
        SessionChatRequest request,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SessionChatStreamEvent>();
        var result = Task.Run(async () =>
        {
            try
            {
                var userMessage = new LocalChatMessage("user", request.Message);
                var messages = session.Messages.Concat([userMessage]).ToList();
                var agentResponse = await agentRuntime.RunAsync(new AgentRunRequest(
                    Agent: request.Agent ?? session.Agent,
                    Message: request.Message,
                    AllowTools: request.AllowTools,
                    PersistToMemory: request.PersistToMemory,
                    ConversationId: session.Id,
                    Model: request.Model ?? session.Model,
                    ReasoningEffort: request.ReasoningEffort,
                    Messages: messages,
                    DelegationDepth: 0,
                    OnEvent: async (runtimeEvent, token) =>
                    {
                        if (runtimeEvent.Tool is null)
                        {
                            return;
                        }

                        var graph = await taskGraphs.RecordToolProgressAsync(session.Id, new TaskGraphToolProgress(
                            Tool: runtimeEvent.Tool,
                            Status: runtimeEvent.Kind == "tool_completed" ? "complete" : "active",
                            Content: runtimeEvent.Content,
                            Arguments: runtimeEvent.Arguments,
                            Result: runtimeEvent.Result), token);

                        await channel.Writer.WriteAsync(new SessionChatStreamEvent("progress", runtimeEvent.Content, Payload: new { runtimeEvent, graph }), token);
                    }), cancellationToken);

                var nextMessages = messages.Concat([new LocalChatMessage("assistant", agentResponse.Response)]).ToList();
                await sessions.SaveAsync(session with
                {
                    Agent = request.Agent ?? session.Agent,
                    Model = request.Model ?? session.Model,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Messages = nextMessages
                }, cancellationToken);
                var graph = await taskGraphs.GetBySessionAsync(session.Id, cancellationToken);

                if (graph is not null)
                {
                    await channel.Writer.WriteAsync(new SessionChatStreamEvent("task_graph", Payload: graph), cancellationToken);
                }

                return new SessionChatResponse(
                    SessionId: session.Id,
                    Response: agentResponse.Response,
                    Messages: nextMessages,
                    Metrics: agentResponse.Metrics,
                    ReasoningSteps: agentResponse.ReasoningSteps ?? [],
                    Summarized: false);
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
        var route = ResolveStreamRoute(agent, request, session);
        var streamMessages = BuildStreamMessages(agent, messages);
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
        var nextMessages = messages.Concat([new LocalChatMessage("assistant", response)]).ToList();
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
            ReasoningSteps: [new ReasoningStep("stream", "Response streamed directly without tools.", DateTimeOffset.UtcNow)],
            Summarized: summarized);

        await sessions.SaveAsync(session with
        {
            Agent = request.Agent ?? session.Agent,
            Model = request.Model ?? session.Model,
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages = nextMessages
        }, cancellationToken);
        var graph = await taskGraphs.RecordRunCompletedAsync(session.Id, response, finalResponse.ReasoningSteps, cancellationToken);
        yield return new SessionChatStreamEvent("task_graph", Payload: graph);
        yield return new SessionChatStreamEvent("final", Result: finalResponse);
    }

    private async IAsyncEnumerable<SessionChatStreamEvent> StreamFinalAnswerAsync(
        string sessionId,
        SessionChatRequest request,
        SessionChatResponse toolResponse,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolResponse.Response))
        {
            yield return new SessionChatStreamEvent("final", Result: toolResponse);
            yield break;
        }

        var session = await sessions.GetAsync(sessionId, cancellationToken);
        var agent = agentRegistry.GetRequiredAgent(request.Agent ?? session.Agent);
        var route = ResolveStreamRoute(agent, request, session);
        var responseBuilder = new StringBuilder();
        LocalChatStreamChunk? finalChunk = null;
        var messages = BuildStreamMessages(agent,
        [
            .. session.Messages.Where(message => message.Role != "assistant"),
            new LocalChatMessage("assistant", toolResponse.Response),
            new LocalChatMessage("user", "Stream the final user-facing answer now. Preserve the tool findings exactly, but do not mention internal protocol details.")
        ]);

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

        var rewrittenMessages = session.Messages.Take(session.Messages.Count - 1).Concat([new LocalChatMessage("assistant", finalText)]).ToList();
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
        var graph = await taskGraphs.RecordRunCompletedAsync(session.Id, finalText, result.ReasoningSteps, cancellationToken);

        yield return new SessionChatStreamEvent("task_graph", Payload: graph);
        yield return new SessionChatStreamEvent("final", Result: result);
    }

    private bool ShouldSummarize(IReadOnlyList<LocalChatMessage> messages)
    {
        var triggerTokens = _options.Orchestration.ContextMaxEstimatedTokens * _options.Orchestration.ContextSummaryTriggerPercent / 100;
        return ContextEstimator.EstimateTokens(messages) >= triggerTokens;
    }

    private async Task<string> SummarizeAsync(AssistantSession session, IReadOnlyList<LocalChatMessage> messages, CancellationToken cancellationToken)
    {
        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: session.Model,
            Messages:
            [
                new LocalChatMessage("system", "Summarize this conversation for future context. Preserve goals, decisions, constraints, tool findings, and unresolved tasks. Be concise."),
                new LocalChatMessage("user", string.Join("\n", messages.Select(message => $"{message.Role}: {message.Content}")))
            ],
            Temperature: 0.2), cancellationToken);

        return response.Response;
    }

    private ModelRoute ResolveStreamRoute(AgentDefinition agent, SessionChatRequest request, AssistantSession session)
    {
        return modelRouter.Resolve(new ModelRouteRequest(agent, request.Message, request.Model ?? session.Model, request.ReasoningEffort));
    }

    private IReadOnlyList<LocalChatMessage> BuildStreamMessages(AgentDefinition agent, IReadOnlyList<LocalChatMessage> messages)
    {
        var systemPrompt = $"{_options.SystemPrompt}\n\n{agent.SystemPrompt}\n\nTools are disabled for this streaming response. Answer directly.";

        return [new LocalChatMessage("system", systemPrompt), .. messages.Where(message => message.Role != "system")];
    }

    private sealed record ToolRunStream(IAsyncEnumerable<SessionChatStreamEvent> Events, Task<SessionChatResponse> Result);
}
