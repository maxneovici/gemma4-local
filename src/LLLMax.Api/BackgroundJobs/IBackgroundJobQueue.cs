namespace LLLMax.Api.BackgroundJobs;

public interface IBackgroundJobQueue
{
    ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken);

    ValueTask<string> DequeueAsync(CancellationToken cancellationToken);
}
