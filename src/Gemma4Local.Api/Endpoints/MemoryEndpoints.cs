using Gemma4Local.Api.Memory;

namespace Gemma4Local.Api.Endpoints;

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

        return app;
    }
}
