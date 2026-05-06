using LLLMax.Api.Agents;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Sessions;

public sealed class AssistantOrchestrator(
    IAssistantSessionStore sessions,
    IAgentRuntime agentRuntime,
    ILocalChatClient chatClient,
    IOptions<LocalAiOptions> options) : IAssistantOrchestrator
{
    private readonly LocalAiOptions _options = options.Value;

    public async Task<SessionChatResponse> ChatAsync(string sessionId, SessionChatRequest request, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(sessionId, cancellationToken);
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
            Messages: messages), cancellationToken);

        var nextMessages = messages.Concat([new LocalChatMessage("assistant", agentResponse.Response)]).ToList();
        await sessions.SaveAsync(session with
        {
            Agent = request.Agent ?? session.Agent,
            Model = request.Model ?? session.Model,
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages = nextMessages
        }, cancellationToken);

        return new SessionChatResponse(
            SessionId: session.Id,
            Response: agentResponse.Response,
            Messages: nextMessages,
            Metrics: agentResponse.Metrics,
            ReasoningSteps: agentResponse.ReasoningSteps ?? [],
            Summarized: summarized);
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
}
