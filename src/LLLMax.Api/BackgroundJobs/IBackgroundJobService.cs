namespace LLLMax.Api.BackgroundJobs;

public interface IBackgroundJobService
{
    Task<BackgroundJob> EnqueueAsync(BackgroundJobCreateRequest request, CancellationToken cancellationToken);

    Task<BackgroundJob?> GetAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BackgroundJob>> ListAsync(CancellationToken cancellationToken);

    Task<BackgroundJob> CancelAsync(string id, CancellationToken cancellationToken);
}
