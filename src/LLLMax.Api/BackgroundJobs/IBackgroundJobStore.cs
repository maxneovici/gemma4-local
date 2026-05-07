namespace LLLMax.Api.BackgroundJobs;

public interface IBackgroundJobStore
{
    Task<BackgroundJob> CreateAsync(BackgroundJobCreateRequest request, CancellationToken cancellationToken);

    Task<BackgroundJob?> GetAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BackgroundJob>> ListAsync(CancellationToken cancellationToken);

    Task<BackgroundJob> SaveAsync(BackgroundJob job, CancellationToken cancellationToken);
}
