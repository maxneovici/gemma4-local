namespace LLLMax.Api.Sessions;

using LLLMax.Api.Models;

public interface IAssistantSessionStore
{
    Task<AssistantSession> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionListResponse>> ListAsync(CancellationToken cancellationToken);

    Task<AssistantSession> GetAsync(string id, CancellationToken cancellationToken);

    Task SaveAsync(AssistantSession session, CancellationToken cancellationToken);

    Task AppendMessageAsync(string id, LocalChatMessage message, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);

    Task DeleteAllAsync(CancellationToken cancellationToken);
}
