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

        group.MapGet("/collections/{collection}/records/{id}", async (string collection, string id, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
        {
            var record = await memoryStore.GetRecordAsync(collection, id, cancellationToken);
            return record is null ? Results.NotFound() : Results.Ok(record);
        });

        group.MapPut("/collections/{collection}/records/{id}", async (string collection, string id, MemoryRecordUpdateRequest request, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
        {
            try
            {
                var record = await memoryStore.UpdateRecordAsync(collection, id, request, cancellationToken);
                return record is null ? Results.NotFound() : Results.Ok(record);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        group.MapDelete("/collections/{collection}/records/{id}", async (string collection, string id, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
            Results.Ok(await memoryStore.DeleteRecordAsync(collection, id, cancellationToken)));

        group.MapGet("/collections/{collection}/groups", async (string collection, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
        {
            var inspected = await memoryStore.InspectCollectionAsync(collection, new MemoryCollectionInspectRequest(Limit: 100), cancellationToken);
            var tenants = inspected.Records
                .GroupBy(record => GetMetadata(record.Metadata, "tenant") ?? "unscoped", StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(tenantGroup => new MemoryTenantGroup(
                    Tenant: tenantGroup.Key,
                    Count: tenantGroup.Count(),
                    Categories: tenantGroup
                        .GroupBy(record => GetMetadata(record.Metadata, "category") ?? "uncategorized", StringComparer.OrdinalIgnoreCase)
                        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(categoryGroup => new MemoryCategoryGroup(categoryGroup.Key, categoryGroup.Count()))
                        .ToList()))
                .ToList();

            return Results.Ok(new MemoryCollectionGroupResponse(inspected.Collection, inspected.Records.Count, tenants));
        });

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

    private static string? GetMetadata(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
