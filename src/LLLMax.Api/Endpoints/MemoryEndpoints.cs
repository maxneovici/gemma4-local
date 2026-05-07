using LLLMax.Api.Memory;

namespace LLLMax.Api.Endpoints;

public static class MemoryEndpoints
{
    public static IEndpointRouteBuilder MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/memory");

        group.MapPost("/upsert", async (MemoryUpsertRequest request, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await memoryStore.UpsertAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        group.MapPost("/search", async (MemorySearchRequest request, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await memoryStore.SearchAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        group.MapGet("/stats", async (ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
            Results.Ok(await memoryStore.GetStatsAsync(cancellationToken)));

        group.MapGet("/collections", async (ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
            Results.Ok(await memoryStore.ListCollectionsAsync(cancellationToken)));

        group.MapGet("/collections/{collection}", async (string collection, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
        {
            var detail = await memoryStore.GetCollectionAsync(collection, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPost("/collections/{collection}/inspect", async (string collection, MemoryCollectionInspectRequest request, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
            Results.Ok(await memoryStore.InspectCollectionAsync(collection, request, cancellationToken)));

        group.MapDelete("/collections/{collection}", async (string collection, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
            Results.Ok(await memoryStore.DeleteCollectionAsync(collection, cancellationToken)));

        group.MapGet("/consolidation/jobs", async (IMemoryConsolidationService consolidation, CancellationToken cancellationToken) =>
            Results.Ok(await consolidation.ListJobsAsync(cancellationToken)));

        group.MapPost("/consolidation", async (MemoryConsolidationRequest request, IMemoryConsolidationService consolidation, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await consolidation.ConsolidateSessionAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        return app;
    }
}
