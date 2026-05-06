using LLLMax.Api.Tasks;

namespace LLLMax.Api.Endpoints;

public static class TaskGraphEndpoints
{
    public static IEndpointRouteBuilder MapTaskGraphEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/task-graphs");

        group.MapGet("/", async (ITaskGraphService taskGraphs, CancellationToken cancellationToken) =>
            Results.Ok(await taskGraphs.ListAsync(cancellationToken)));

        group.MapGet("/sessions/{sessionId}", async (string sessionId, ITaskGraphService taskGraphs, CancellationToken cancellationToken) =>
        {
            var graph = await taskGraphs.GetBySessionAsync(sessionId, cancellationToken);
            return graph is null ? Results.NotFound() : Results.Ok(graph);
        });

        group.MapPost("/", async (TaskGraphCreateRequest request, ITaskGraphService taskGraphs, CancellationToken cancellationToken) =>
            Results.Ok(await taskGraphs.EnsureForSessionAsync(request.SessionId, request.Goal, cancellationToken)));

        group.MapPatch("/{id}", async (string id, TaskGraphUpdateRequest request, ITaskGraphService taskGraphs, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await taskGraphs.UpdateAsync(id, request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        group.MapPost("/{id}/artifacts", async (string id, TaskGraphArtifactRequest request, ITaskGraphService taskGraphs, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await taskGraphs.AddArtifactAsync(id, request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        return app;
    }
}
