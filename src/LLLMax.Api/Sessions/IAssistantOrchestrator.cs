namespace LLLMax.Api.Sessions;

public interface IAssistantOrchestrator
{
    Task<SessionChatResponse> ChatAsync(string sessionId, SessionChatRequest request, CancellationToken cancellationToken);
}
