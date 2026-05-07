using System.Text.Json;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.BackgroundJobs;

public sealed class EfBackgroundJobStore(
    IDbContextFactory<LocalDbContext> dbFactory,
    LocalDataPaths paths,
    ILogger<EfBackgroundJobStore> logger) : IBackgroundJobStore
{
    private const string ImportMarker = "background_jobs_json_imported";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _imported;

    public async Task<BackgroundJob> CreateAsync(BackgroundJobCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);

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
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<BackgroundJob>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var jobs = await db.BackgroundJobs
            .AsNoTracking()
            .Select(job => ToModel(job))
            .ToListAsync(cancellationToken);

        return jobs.OrderByDescending(job => job.UpdatedAt).ToList();
    }

    public async Task<BackgroundJob> SaveAsync(BackgroundJob job, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.BackgroundJobs.SingleOrDefaultAsync(entity => entity.Id == job.Id, cancellationToken);

            if (existing is null)
            {
                db.BackgroundJobs.Add(ToEntity(job));
            }
            else
            {
                Copy(job, existing);
            }

            await db.SaveChangesAsync(cancellationToken);
            return job;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureImportedAsync(CancellationToken cancellationToken) =>
        await JsonImport.ImportOnceAsync(dbFactory, _gate, () => _imported, () => _imported = true, ImportMarker, ImportJsonAsync, cancellationToken);

    private async Task ImportJsonAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(paths.BackgroundJobsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(file);
                var job = await JsonSerializer.DeserializeAsync<BackgroundJob>(stream, JsonOptions, cancellationToken);

                if (job is null || await db.BackgroundJobs.AnyAsync(existing => existing.Id == job.Id, cancellationToken))
                {
                    continue;
                }

                db.BackgroundJobs.Add(ToEntity(job));
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Could not import background job file {File}", file);
            }
        }
    }

    private static BackgroundJobEntity ToEntity(BackgroundJob job)
    {
        var entity = new BackgroundJobEntity();
        Copy(job, entity);
        return entity;
    }

    private static void Copy(BackgroundJob job, BackgroundJobEntity entity)
    {
        entity.Id = job.Id;
        entity.Kind = job.Kind;
        entity.Status = job.Status;
        entity.Title = job.Title;
        entity.SessionId = job.SessionId;
        entity.Agent = job.Agent;
        entity.PayloadJson = JsonElementValue.Serialize(job.Payload);
        entity.ProgressCurrent = job.ProgressCurrent;
        entity.ProgressTotal = job.ProgressTotal;
        entity.StatusMessage = job.StatusMessage;
        entity.Result = job.Result;
        entity.Error = job.Error;
        entity.NotifySession = job.NotifySession;
        entity.CreatedAt = job.CreatedAt;
        entity.UpdatedAt = job.UpdatedAt;
        entity.CompletedAt = job.CompletedAt;
    }

    private static BackgroundJob ToModel(BackgroundJobEntity entity) =>
        new(
            Id: entity.Id,
            Kind: entity.Kind,
            Status: entity.Status,
            Title: entity.Title,
            SessionId: entity.SessionId,
            Agent: entity.Agent,
            Payload: JsonElementValue.Parse(entity.PayloadJson),
            ProgressCurrent: entity.ProgressCurrent,
            ProgressTotal: entity.ProgressTotal,
            StatusMessage: entity.StatusMessage,
            Result: entity.Result,
            Error: entity.Error,
            NotifySession: entity.NotifySession,
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt,
            CompletedAt: entity.CompletedAt);
}
