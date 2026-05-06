namespace LLLMax.Api.Tasks;

public interface ITaskGraphStore
{
    Task<TaskGraph> SaveAsync(TaskGraph graph, CancellationToken cancellationToken);

    Task<TaskGraph?> GetAsync(string id, CancellationToken cancellationToken);

    Task<TaskGraph?> GetBySessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskGraphSummary>> ListAsync(CancellationToken cancellationToken);
}
