namespace LLLMax.Api.BackgroundJobs;

public interface IBackgroundJobArtifactStore
{
    Task<BackgroundJobArtifact> CreateAsync(string jobId, BackgroundJobArtifactCreateRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<BackgroundJobArtifact>> ListAsync(string jobId, CancellationToken cancellationToken);

    Task<(BackgroundJobArtifact Artifact, string Content)?> GetAsync(string jobId, string artifactId, CancellationToken cancellationToken);
}
