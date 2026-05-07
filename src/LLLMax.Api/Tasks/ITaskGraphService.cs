using LLLMax.Api.Agents;

namespace LLLMax.Api.Tasks;

public interface ITaskGraphService
{
    Task<TaskGraph> EnsureForSessionAsync(string sessionId, string goal, CancellationToken cancellationToken);

    Task<TaskGraph?> GetBySessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskGraphSummary>> ListAsync(CancellationToken cancellationToken);

    Task<TaskGraph> UpdateAsync(string graphId, TaskGraphUpdateRequest request, CancellationToken cancellationToken);

    Task<TaskGraph> AddArtifactAsync(string graphId, TaskGraphArtifactRequest request, CancellationToken cancellationToken);

    Task<TaskGraph> RecordRunStartedAsync(string sessionId, string goal, CancellationToken cancellationToken);

    Task<TaskGraph> RecordToolProgressAsync(string sessionId, TaskGraphToolProgress progress, CancellationToken cancellationToken);

    Task<TaskGraph> RecordRunCompletedAsync(string sessionId, string answer, IReadOnlyList<ReasoningStep> reasoningSteps, CancellationToken cancellationToken);

    TaskGraph SnapshotCurrentTurn(TaskGraph graph);

    Task<TaskGraph> RecordRunBlockedAsync(string sessionId, string blocker, CancellationToken cancellationToken);
}
