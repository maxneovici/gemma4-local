using System.Text.Json;
using LLLMax.Api.Services;

namespace LLLMax.Api.BackgroundJobs;

public sealed class FileBackgroundJobArtifactStore(LocalDataPaths paths) : IBackgroundJobArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<BackgroundJobArtifact> CreateAsync(string jobId, BackgroundJobArtifactCreateRequest request, CancellationToken cancellationToken)
    {
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

        await using var stream = File.Create(GetMetadataPath(jobId, id));
        await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
        return metadata;
    }

    public async Task<IReadOnlyList<BackgroundJobArtifact>> ListAsync(string jobId, CancellationToken cancellationToken)
    {
        var directory = GetJobArtifactDirectory(jobId);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var artifacts = new List<BackgroundJobArtifact>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var artifact = await JsonSerializer.DeserializeAsync<BackgroundJobArtifact>(stream, JsonOptions, cancellationToken);

            if (artifact is not null)
            {
                artifacts.Add(artifact);
            }
        }

        return artifacts.OrderByDescending(artifact => artifact.CreatedAt).ToList();
    }

    public async Task<(BackgroundJobArtifact Artifact, string Content)?> GetAsync(string jobId, string artifactId, CancellationToken cancellationToken)
    {
        var metadataPath = GetMetadataPath(jobId, artifactId);

        if (!File.Exists(metadataPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(metadataPath);
        var artifact = await JsonSerializer.DeserializeAsync<BackgroundJobArtifact>(stream, JsonOptions, cancellationToken);

        if (artifact is null)
        {
            return null;
        }

        var contentPath = Path.Combine(GetJobArtifactDirectory(jobId), artifact.FileName);

        if (!File.Exists(contentPath))
        {
            return null;
        }

        return (artifact, await File.ReadAllTextAsync(contentPath, cancellationToken));
    }

    private string GetJobArtifactDirectory(string jobId) => Path.Combine(paths.BackgroundJobArtifactsDirectory, jobId);

    private string GetMetadataPath(string jobId, string artifactId) => Path.Combine(GetJobArtifactDirectory(jobId), $"{artifactId}.json");
}
