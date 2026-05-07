using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Sessions;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.BackgroundJobs;

public sealed class BackgroundJobWorker(
    IBackgroundJobStore store,
    IBackgroundJobQueue queue,
    BackgroundJobService jobs,
    IEnumerable<IBackgroundJobHandler> handlers,
    IAssistantSessionStore sessions,
    IBackgroundJobArtifactStore artifacts,
    IOptions<LocalAiOptions> options,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    private readonly IReadOnlyDictionary<string, IBackgroundJobHandler> _handlers = handlers.ToDictionary(handler => handler.Kind, StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _concurrency = new(Math.Clamp(options.Value.Orchestration.MaxConcurrentBackgroundJobs, 1, 16));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeuePendingAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var jobId = await queue.DequeueAsync(stoppingToken);
            await _concurrency.WaitAsync(stoppingToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunJobAsync(jobId, stoppingToken);
                }
                finally
                {
                    _concurrency.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task RequeuePendingAsync(CancellationToken cancellationToken)
    {
        foreach (var job in await store.ListAsync(cancellationToken))
        {
            if (job.Status is BackgroundJobStatuses.Queued or BackgroundJobStatuses.Running)
            {
                var queued = job with
                {
                    Status = BackgroundJobStatuses.Queued,
                    StatusMessage = job.Status == BackgroundJobStatuses.Running ? "Requeued after app restart." : job.StatusMessage,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await store.SaveAsync(queued, cancellationToken);
                await queue.EnqueueAsync(job.Id, cancellationToken);
            }
        }
    }

    private async Task RunJobAsync(string jobId, CancellationToken stoppingToken)
    {
        var job = await store.GetAsync(jobId, stoppingToken);

        if (job is null || job.Status == BackgroundJobStatuses.Cancelled)
        {
            return;
        }

        if (!_handlers.TryGetValue(job.Kind, out var handler))
        {
            await CompleteAsync(job, BackgroundJobStatuses.Failed, null, $"No handler is registered for background job kind '{job.Kind}'.", stoppingToken);
            return;
        }

        using var cts = jobs.RegisterRunning(job.Id, stoppingToken);

        try
        {
            var now = DateTimeOffset.UtcNow;
            job = await store.SaveAsync(job with
            {
                Status = BackgroundJobStatuses.Running,
                StatusMessage = "Running.",
                UpdatedAt = now
            }, stoppingToken);

            var context = new BackgroundJobContext(store, artifacts, job);
            var result = await handler.RunAsync(job, context, cts.Token);
            job = await store.GetAsync(job.Id, stoppingToken) ?? job;

            if (job.Status == BackgroundJobStatuses.Cancelled)
            {
                await NotifySessionAsync(job, "Background job cancelled.", stoppingToken);
                return;
            }

            await CompleteAsync(job, BackgroundJobStatuses.Completed, result, null, stoppingToken);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            job = await store.GetAsync(job.Id, CancellationToken.None) ?? job;
            await CompleteAsync(job, BackgroundJobStatuses.Cancelled, null, null, CancellationToken.None);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Background job {JobId} failed", job.Id);
            job = await store.GetAsync(job.Id, CancellationToken.None) ?? job;
            await CompleteAsync(job, BackgroundJobStatuses.Failed, null, exception.Message, CancellationToken.None);
        }
        finally
        {
            jobs.UnregisterRunning(job.Id);
        }
    }

    private async Task CompleteAsync(BackgroundJob job, string status, string? result, string? error, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var completed = await store.SaveAsync(job with
        {
            Status = status,
            StatusMessage = status switch
            {
                BackgroundJobStatuses.Completed => "Completed.",
                BackgroundJobStatuses.Cancelled => "Cancelled.",
                BackgroundJobStatuses.Failed => "Failed.",
                _ => job.StatusMessage
            },
            Result = result ?? job.Result,
            Error = error,
            UpdatedAt = now,
            CompletedAt = now
        }, cancellationToken);

        await NotifySessionAsync(completed, BuildNotification(completed), cancellationToken);
    }

    private async Task NotifySessionAsync(BackgroundJob job, string message, CancellationToken cancellationToken)
    {
        if (!job.NotifySession || string.IsNullOrWhiteSpace(job.SessionId))
        {
            return;
        }

        try
        {
            await sessions.AppendMessageAsync(job.SessionId, new LocalChatMessage("assistant", message), cancellationToken);
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("Background job {JobId} could not notify missing session {SessionId}", job.Id, job.SessionId);
        }
    }

    private static string BuildNotification(BackgroundJob job) =>
        job.Status switch
        {
            BackgroundJobStatuses.Completed => $"Background job completed: {job.Title ?? job.Kind}.\n\n{job.Result}",
            BackgroundJobStatuses.Failed => $"Background job failed: {job.Title ?? job.Kind}.\n\n{job.Error}",
            BackgroundJobStatuses.Cancelled => $"Background job cancelled: {job.Title ?? job.Kind}.",
            _ => $"Background job updated: {job.Title ?? job.Kind} is {job.Status}."
        };

    private sealed class BackgroundJobContext(IBackgroundJobStore store, IBackgroundJobArtifactStore artifacts, BackgroundJob initialJob) : IBackgroundJobContext
    {
        private BackgroundJob _job = initialJob;

        public async Task ReportAsync(BackgroundJobProgress progress, CancellationToken cancellationToken)
        {
            var current = Math.Max(0, progress.Current);
            var total = Math.Max(0, progress.Total);
            _job = await store.SaveAsync(_job with
            {
                ProgressCurrent = current,
                ProgressTotal = total,
                StatusMessage = progress.Message,
                Result = progress.Result ?? _job.Result,
                Error = progress.Error ?? _job.Error,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        public Task<BackgroundJobArtifact> AddArtifactAsync(BackgroundJobArtifactCreateRequest request, CancellationToken cancellationToken) =>
            artifacts.CreateAsync(_job.Id, request, cancellationToken);
    }
}
