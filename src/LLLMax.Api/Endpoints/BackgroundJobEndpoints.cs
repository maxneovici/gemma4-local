using LLLMax.Api.BackgroundJobs;

namespace LLLMax.Api.Endpoints;

public static class BackgroundJobEndpoints
{
    public static IEndpointRouteBuilder MapBackgroundJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/background-jobs");

        group.MapGet("/", async (IBackgroundJobService jobs, CancellationToken cancellationToken) =>
        {
            var items = await jobs.ListAsync(cancellationToken);
            return Results.Ok(items.Select(job => new BackgroundJobListResponse(
                job.Id,
                job.Kind,
                job.Status,
                job.Title,
                job.SessionId,
                job.ProgressCurrent,
                job.ProgressTotal,
                job.StatusMessage,
                job.Error,
                job.UpdatedAt)));
        });

        group.MapGet("/{id}", async (string id, IBackgroundJobService jobs, CancellationToken cancellationToken) =>
        {
            var job = await jobs.GetAsync(id, cancellationToken);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });

        group.MapGet("/{id}/artifacts", async (string id, IBackgroundJobArtifactStore artifacts, CancellationToken cancellationToken) =>
            Results.Ok(await artifacts.ListAsync(id, cancellationToken)));

        group.MapGet("/{id}/artifacts/{artifactId}", async (string id, string artifactId, IBackgroundJobArtifactStore artifacts, CancellationToken cancellationToken) =>
        {
            var result = await artifacts.GetAsync(id, artifactId, cancellationToken);
            return result is null
                ? Results.NotFound()
                : Results.Text(result.Value.Content, result.Value.Artifact.ContentType);
        });

        group.MapPost("/", async (BackgroundJobCreateRequest request, IBackgroundJobService jobs, CancellationToken cancellationToken) =>
            Results.Ok(await jobs.EnqueueAsync(request, cancellationToken)));

        group.MapPost("/{id}/cancel", async (string id, IBackgroundJobService jobs, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await jobs.CancelAsync(id, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        return app;
    }
}
