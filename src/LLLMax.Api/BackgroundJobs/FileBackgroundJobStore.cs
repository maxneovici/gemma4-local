using System.Text.Json;
using LLLMax.Api.Services;

namespace LLLMax.Api.BackgroundJobs;

public sealed class FileBackgroundJobStore(LocalDataPaths paths) : IBackgroundJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<BackgroundJob> CreateAsync(BackgroundJobCreateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Kind))
        {
            throw new ArgumentException("Background job kind is required.", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var job = new BackgroundJob(
            Id: Guid.NewGuid().ToString("n"),
            Kind: request.Kind.Trim(),
            Status: BackgroundJobStatuses.Queued,
            Title: string.IsNullOrWhiteSpace(request.Title) ? request.Kind.Trim() : request.Title.Trim(),
            SessionId: request.SessionId,
            Agent: request.Agent,
            Payload: request.Payload.Clone(),
            ProgressCurrent: 0,
            ProgressTotal: 0,
            StatusMessage: "Queued.",
            Result: null,
            Error: null,
            NotifySession: request.NotifySession,
            CreatedAt: now,
            UpdatedAt: now);

        return await SaveAsync(job, cancellationToken);
    }

    public async Task<BackgroundJob?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var file = GetPath(id);

        if (!File.Exists(file))
        {
            return null;
        }

        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<BackgroundJob>(stream, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<BackgroundJob>> ListAsync(CancellationToken cancellationToken)
    {
        var jobs = new List<BackgroundJob>();

        foreach (var file in Directory.EnumerateFiles(paths.BackgroundJobsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var job = await JsonSerializer.DeserializeAsync<BackgroundJob>(stream, JsonOptions, cancellationToken);

            if (job is not null)
            {
                jobs.Add(job);
            }
        }

        return jobs.OrderByDescending(job => job.UpdatedAt).ToList();
    }

    public async Task<BackgroundJob> SaveAsync(BackgroundJob job, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var stream = File.Create(GetPath(job.Id));
            await JsonSerializer.SerializeAsync(stream, job with { Payload = job.Payload.Clone() }, JsonOptions, cancellationToken);
            return job;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetPath(string id) => Path.Combine(paths.BackgroundJobsDirectory, $"{id}.json");
}
