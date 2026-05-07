using System.Text.Json;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.Memory;

public sealed class EfMemoryConsolidationJobStore(
    IDbContextFactory<LocalDbContext> dbFactory,
    LocalDataPaths paths,
    ILogger<EfMemoryConsolidationJobStore> logger) : IMemoryConsolidationJobStore
{
    private const string ImportMarker = "memory_consolidation_jobs_json_imported";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _imported;

    public async Task<MemoryConsolidationJob> SaveAsync(MemoryConsolidationJob job, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.MemoryConsolidationJobs.SingleOrDefaultAsync(entity => entity.Id == job.Id, cancellationToken);

            if (existing is null)
            {
                db.MemoryConsolidationJobs.Add(ToEntity(job));
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

    public async Task<IReadOnlyList<MemoryConsolidationJob>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var jobs = await db.MemoryConsolidationJobs
            .AsNoTracking()
            .Select(job => ToModel(job))
            .ToListAsync(cancellationToken);

        return jobs.OrderByDescending(job => job.UpdatedAt).ToList();
    }

    private async Task EnsureImportedAsync(CancellationToken cancellationToken) =>
        await JsonImport.ImportOnceAsync(dbFactory, _gate, () => _imported, () => _imported = true, ImportMarker, ImportJsonAsync, cancellationToken);

    private async Task ImportJsonAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(paths.ConsolidationJobsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(file);
                var job = await JsonSerializer.DeserializeAsync<MemoryConsolidationJob>(stream, JsonOptions, cancellationToken);

                if (job is null || await db.MemoryConsolidationJobs.AnyAsync(existing => existing.Id == job.Id, cancellationToken))
                {
                    continue;
                }

                db.MemoryConsolidationJobs.Add(ToEntity(job));
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Could not import memory consolidation job file {File}", file);
            }
        }
    }

    private static MemoryConsolidationJobEntity ToEntity(MemoryConsolidationJob job)
    {
        var entity = new MemoryConsolidationJobEntity();
        Copy(job, entity);
        return entity;
    }

    private static void Copy(MemoryConsolidationJob job, MemoryConsolidationJobEntity entity)
    {
        entity.Id = job.Id;
        entity.SessionId = job.SessionId;
        entity.Status = job.Status;
        entity.CreatedAt = job.CreatedAt;
        entity.UpdatedAt = job.UpdatedAt;
        entity.MemoriesWritten = job.MemoriesWritten;
        entity.Summary = job.Summary;
        entity.Error = job.Error;
        entity.TaskGraphId = job.TaskGraphId;
    }

    private static MemoryConsolidationJob ToModel(MemoryConsolidationJobEntity entity) =>
        new(
            Id: entity.Id,
            SessionId: entity.SessionId,
            Status: entity.Status,
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt,
            MemoriesWritten: entity.MemoriesWritten,
            Summary: entity.Summary,
            Error: entity.Error,
            TaskGraphId: entity.TaskGraphId);
}
