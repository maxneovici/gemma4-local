using System.Text.Json;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.BackgroundJobs;

public sealed class EfBackgroundJobArtifactStore(
    IDbContextFactory<LocalDbContext> dbFactory,
    LocalDataPaths paths,
    ILogger<EfBackgroundJobArtifactStore> logger) : IBackgroundJobArtifactStore
{
    private const string ImportMarker = "background_job_artifacts_json_imported";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _imported;

    public async Task<BackgroundJobArtifact> CreateAsync(string jobId, BackgroundJobArtifactCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("n");
        var fileName = string.IsNullOrWhiteSpace(request.FileName) ? $"{id}.txt" : Path.GetFileName(request.FileName);
        var directory = GetJobArtifactDirectory(jobId);
        Directory.CreateDirectory(directory);
        var contentPath = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(contentPath, request.Content, cancellationToken);
        var metadata = new BackgroundJobArtifact(
            Id: id,
            JobId: jobId,
            Kind: string.IsNullOrWhiteSpace(request.Kind) ? "artifact" : request.Kind.Trim(),
            Title: string.IsNullOrWhiteSpace(request.Title) ? fileName : request.Title.Trim(),
            ContentType: string.IsNullOrWhiteSpace(request.ContentType) ? "text/plain" : request.ContentType.Trim(),
            FileName: fileName,
            Bytes: new FileInfo(contentPath).Length,
            CreatedAt: DateTimeOffset.UtcNow);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.BackgroundJobArtifacts.Add(ToEntity(metadata));
            await db.SaveChangesAsync(cancellationToken);
            return metadata;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackgroundJobArtifact>> ListAsync(string jobId, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var artifacts = await db.BackgroundJobArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.JobId == jobId)
            .Select(artifact => ToModel(artifact))
            .ToListAsync(cancellationToken);

        return artifacts.OrderByDescending(artifact => artifact.CreatedAt).ToList();
    }

    public async Task<(BackgroundJobArtifact Artifact, string Content)?> GetAsync(string jobId, string artifactId, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.BackgroundJobArtifacts
            .AsNoTracking()
            .SingleOrDefaultAsync(artifact => artifact.JobId == jobId && artifact.Id == artifactId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var artifact = ToModel(entity);
        var contentPath = Path.Combine(GetJobArtifactDirectory(jobId), artifact.FileName);

        if (!File.Exists(contentPath))
        {
            return null;
        }

        return (artifact, await File.ReadAllTextAsync(contentPath, cancellationToken));
    }

    private async Task EnsureImportedAsync(CancellationToken cancellationToken) =>
        await JsonImport.ImportOnceAsync(dbFactory, _gate, () => _imported, () => _imported = true, ImportMarker, ImportJsonAsync, cancellationToken);

    private async Task ImportJsonAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.BackgroundJobArtifactsDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(paths.BackgroundJobArtifactsDirectory, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(file);
                var artifact = await JsonSerializer.DeserializeAsync<BackgroundJobArtifact>(stream, JsonOptions, cancellationToken);

                if (artifact is null || await db.BackgroundJobArtifacts.AnyAsync(existing => existing.Id == artifact.Id, cancellationToken))
                {
                    continue;
                }

                db.BackgroundJobArtifacts.Add(ToEntity(artifact));
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Could not import background job artifact metadata file {File}", file);
            }
        }
    }

    private string GetJobArtifactDirectory(string jobId) => Path.Combine(paths.BackgroundJobArtifactsDirectory, jobId);

    private static BackgroundJobArtifactEntity ToEntity(BackgroundJobArtifact artifact) =>
        new()
        {
            Id = artifact.Id,
            JobId = artifact.JobId,
            Kind = artifact.Kind,
            Title = artifact.Title,
            ContentType = artifact.ContentType,
            FileName = artifact.FileName,
            Bytes = artifact.Bytes,
            CreatedAt = artifact.CreatedAt
        };

    private static BackgroundJobArtifact ToModel(BackgroundJobArtifactEntity entity) =>
        new(
            Id: entity.Id,
            JobId: entity.JobId,
            Kind: entity.Kind,
            Title: entity.Title,
            ContentType: entity.ContentType,
            FileName: entity.FileName,
            Bytes: entity.Bytes,
            CreatedAt: entity.CreatedAt);
}
