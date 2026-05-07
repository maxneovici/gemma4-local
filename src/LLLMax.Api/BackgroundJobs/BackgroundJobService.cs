using System.Collections.Concurrent;

namespace LLLMax.Api.BackgroundJobs;

public sealed class BackgroundJobService(IBackgroundJobStore store, IBackgroundJobQueue queue) : IBackgroundJobService
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BackgroundJob> EnqueueAsync(BackgroundJobCreateRequest request, CancellationToken cancellationToken)
    {
        var job = await store.CreateAsync(request, cancellationToken);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        return job;
    }

    public Task<BackgroundJob?> GetAsync(string id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<BackgroundJob>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public async Task<BackgroundJob> CancelAsync(string id, CancellationToken cancellationToken)
    {
        var job = await store.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Background job '{id}' does not exist.");

        if (job.Status is BackgroundJobStatuses.Completed or BackgroundJobStatuses.Failed or BackgroundJobStatuses.Cancelled)
        {
            return job;
        }

        if (_running.TryGetValue(id, out var running))
        {
            await running.CancelAsync();
        }

        var now = DateTimeOffset.UtcNow;
        return await store.SaveAsync(job with
        {
            Status = BackgroundJobStatuses.Cancelled,
            StatusMessage = "Cancellation requested.",
            UpdatedAt = now,
            CompletedAt = job.Status == BackgroundJobStatuses.Queued ? now : job.CompletedAt
        }, cancellationToken);
    }

    public CancellationTokenSource RegisterRunning(string id, CancellationToken parentToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        _running[id] = cts;
        return cts;
    }

    public void UnregisterRunning(string id)
    {
        if (_running.TryRemove(id, out var cts))
        {
            cts.Dispose();
        }
    }
}
