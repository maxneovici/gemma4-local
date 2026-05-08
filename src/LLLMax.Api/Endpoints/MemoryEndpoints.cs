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
                return Results.Ok(string.IsNullOrWhiteSpace(request.Collection)
                    ? await SearchDefaultMemoryBandsAsync(request, memoryStore, cancellationToken)
                    : await memoryStore.SearchAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        group.MapGet("/stats", async (ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
            Results.Ok(await memoryStore.GetStatsAsync(cancellationToken)));

        group.MapGet("/profile", async (IMemoryReflectionService reflection, CancellationToken cancellationToken) =>
            Results.Ok(await reflection.GetProfileAsync(cancellationToken)));

        group.MapPost("/profile/reflect", async (MemoryReflectionRequest request, IMemoryReflectionService reflection, CancellationToken cancellationToken) =>
            Results.Ok(await reflection.ReflectAsync(request, cancellationToken)));

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

    private static async Task<IReadOnlyList<MemorySearchResult>> SearchDefaultMemoryBandsAsync(MemorySearchRequest request, ILocalMemoryStore memoryStore, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var limit = Math.Clamp(request.Limit, 6, 20);
        var bands = new List<(MemorySearchResult Result, int Priority)>();

        await AddBandAsync(bands, memoryStore, "profile_canonical", request.Query, limit, -1, MergeFilter(request.Filter, new Dictionary<string, string> { ["kind"] = "canonical_profile_fact" }), cancellationToken);
        await AddBandAsync(bands, memoryStore, "core", request.Query, limit, 0, MergeFilter(request.Filter, new Dictionary<string, string> { ["category"] = "profile" }), cancellationToken);
        await AddBandAsync(bands, memoryStore, "profile", request.Query, limit, 1, request.Filter, cancellationToken);
        await AddBandAsync(bands, memoryStore, "coordinator", request.Query, limit, 2, request.Filter, cancellationToken);
        await AddBandAsync(bands, memoryStore, "core", request.Query, Math.Max(1, limit / 2), 3, MergeFilter(request.Filter, new Dictionary<string, string> { ["category"] = "session_summary" }), cancellationToken);
        await ExpandRelatedCoreProfileMemoriesAsync(bands, memoryStore, cancellationToken);

        return bands
            .GroupBy(item => item.Result.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(RankDefaultMemoryResult).First())
            .OrderByDescending(RankDefaultMemoryResult)
            .Select(item => item.Result)
            .Take(limit)
            .ToList();
    }

    private static double RankDefaultMemoryResult((MemorySearchResult Result, int Priority) item) =>
        item.Result.Score + (item.Priority < 0 ? 0.05 : 0) - (Math.Max(item.Priority, 0) * 0.05);

    private static async Task AddBandAsync(
        ICollection<(MemorySearchResult Result, int Priority)> bands,
        ILocalMemoryStore memoryStore,
        string collection,
        string query,
        int limit,
        int priority,
        IReadOnlyDictionary<string, string>? filter,
        CancellationToken cancellationToken)
    {
        var results = await memoryStore.SearchAsync(new MemorySearchRequest(collection, query, limit, filter), cancellationToken);

        foreach (var result in results)
        {
            bands.Add((WithCollectionMetadata(result, collection), priority));
        }
    }

    private static IReadOnlyDictionary<string, string>? MergeFilter(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string> right)
    {
        if (left is null || left.Count == 0)
        {
            return right;
        }

        var merged = left.ToDictionary(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in right)
        {
            merged.TryAdd(pair.Key, pair.Value);
        }

        return merged;
    }

    private static MemorySearchResult WithCollectionMetadata(MemorySearchResult result, string collection)
    {
        var metadata = result.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
        metadata.TryAdd("collection", collection);
        return result with { Metadata = metadata };
    }

    private static async Task ExpandRelatedCoreProfileMemoriesAsync(ICollection<(MemorySearchResult Result, int Priority)> bands, ILocalMemoryStore memoryStore, CancellationToken cancellationToken)
    {
        var seedSessions = bands
            .Where(item => GetMetadata(item.Result.Metadata, "collection")?.Equals("core", StringComparison.OrdinalIgnoreCase) == true)
            .Where(item => GetMetadata(item.Result.Metadata, "category")?.Equals("profile", StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => new { SessionId = GetMetadata(item.Result.Metadata, "sessionId"), item.Result.Score, item.Priority })
            .Where(item => !string.IsNullOrWhiteSpace(item.SessionId))
            .GroupBy(item => item.SessionId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Priority).ThenByDescending(item => item.Score).First())
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Score)
            .Take(2)
            .ToList();

        foreach (var seed in seedSessions)
        {
            var related = await memoryStore.InspectCollectionAsync("core", new MemoryCollectionInspectRequest(
                Limit: 12,
                Filter: new Dictionary<string, string>
                {
                    ["sessionId"] = seed.SessionId!,
                    ["category"] = "profile"
                }), cancellationToken);

            foreach (var record in related.Records)
            {
                var metadata = record.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
                metadata.TryAdd("collection", "core");
                bands.Add((new MemorySearchResult(record.Id, record.TextPreview, Math.Max(0, seed.Score - 0.01), metadata), seed.Priority));
            }
        }
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
