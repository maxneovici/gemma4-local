namespace LLLMax.Api.Sessions;

public interface IAssistantSessionStore
{
    Task<AssistantSession> CreateAsync(SessionCreateRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionListResponse>> ListAsync(CancellationToken cancellationToken);

    Task<AssistantSession> GetAsync(string id, CancellationToken cancellationToken);

    Task SaveAsync(AssistantSession session, CancellationToken cancellationToken);

    Task DeleteAllAsync(CancellationToken cancellationToken);
}
