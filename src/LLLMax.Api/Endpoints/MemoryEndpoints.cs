using LLLMax.Api.Memory;
using LLLMax.Api.Options;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

        group.MapPost("/search", async (MemorySearchRequest request, ILocalMemoryStore memoryStore, IMemoryRecallPlanner recallPlanner, CancellationToken cancellationToken) =>
        {
            try
            {
                var normalizedRequest = string.IsNullOrWhiteSpace(request.Collection)
                    ? request with { Collection = MemoryLayers.Memory }
                    : request;
                var collection = normalizedRequest.Collection ?? MemoryLayers.Memory;
                var results = collection.Equals(MemoryLayers.Memory, StringComparison.OrdinalIgnoreCase)
                    ? await SearchDefaultMemoryBandsAsync(normalizedRequest, memoryStore, recallPlanner, cancellationToken)
                    : await memoryStore.SearchAsync(normalizedRequest, cancellationToken);

                return Results.Ok(results.Where(result => !MemoryRecallPolicy.IsNoiseResult(result)).ToList());
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

        group.MapPost("/reset-all", async (MemoryResetAllRequest request, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
        {
            if (!request.Confirm.Equals("RESET ALL VECTOR MEMORY", StringComparison.Ordinal))
            {
                return Results.BadRequest(new { Error = "Confirmation must be exactly 'RESET ALL VECTOR MEMORY'." });
            }

            var collections = await memoryStore.ListCollectionsAsync(cancellationToken);
            var deleted = new List<string>();

            foreach (var collection in collections)
            {
                var result = await memoryStore.DeleteCollectionAsync(collection.Name, cancellationToken);

                if (result.Deleted)
                {
                    deleted.Add(collection.Name);
                }
            }

            return Results.Ok(new MemoryResetAllResponse(deleted.Count, deleted));
        });

        group.MapPost("/reset-everything", async (MemoryResetEverythingRequest request, ILocalMemoryStore memoryStore, IDbContextFactory<LocalDbContext> dbFactory, CancellationToken cancellationToken) =>
        {
            if (!request.Confirm.Equals("RESET EVERYTHING", StringComparison.Ordinal))
            {
                return Results.BadRequest(new { Error = "Confirmation must be exactly 'RESET EVERYTHING'." });
            }

            var collections = await memoryStore.ListCollectionsAsync(cancellationToken);
            var deletedCollections = new List<string>();

            foreach (var collection in collections)
            {
                var result = await memoryStore.DeleteCollectionAsync(collection.Name, cancellationToken);

                if (result.Deleted)
                {
                    deletedCollections.Add(collection.Name);
                }
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var deletedRows = 0;
            deletedRows += await db.McpTools.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.McpServers.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.ApiIntegrations.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.MemoryConsolidationJobs.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.TaskGraphs.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.Approvals.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.Documents.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.BackgroundJobArtifacts.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.BackgroundJobs.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.UserProfiles.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.SessionMessages.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.Sessions.ExecuteDeleteAsync(cancellationToken);
            deletedRows += await db.AppMetadata.ExecuteDeleteAsync(cancellationToken);

            return Results.Ok(new MemoryResetEverythingResponse(deletedCollections.Count, deletedCollections, deletedRows));
        });

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

    private static async Task<IReadOnlyList<MemorySearchResult>> SearchDefaultMemoryBandsAsync(MemorySearchRequest request, ILocalMemoryStore memoryStore, IMemoryRecallPlanner recallPlanner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var limit = Math.Clamp(request.Limit, 6, 20);
        var bands = new List<(MemorySearchResult Result, int Priority, int QueryIndex)>();
        var collections = GetDefaultCollections();
        var plan = await recallPlanner.PlanAsync(request.Query, null, cancellationToken);
        var queries = MemoryRecallPolicy.BuildQueries(request.Query, null, plan);

        for (var queryIndex = 0; queryIndex < queries.Count; queryIndex++)
        {
            var query = queries[queryIndex];

            foreach (var collection in collections)
            {
                await AddBandAsync(bands, memoryStore, collection.Name, query, limit, collection.Priority, queryIndex, MergeFilter(request.Filter, collection.Filter ?? new Dictionary<string, string>()), cancellationToken);
            }
        }

        await ExpandRelatedMemoriesAsync(bands, memoryStore, cancellationToken);

        var candidates = MemoryRecallPolicy.SelectTopDiverse(bands, queries, plan, Math.Max(limit * 4, limit));
        var ids = await recallPlanner.RerankAsync(request.Query, plan, candidates, limit, cancellationToken);
        return OrderByIds(candidates, ids, limit);
    }

    private static IReadOnlyList<MemoryCollectionBand> GetDefaultCollections() =>
    [
        new(MemoryLayers.Memory, 0, null)
    ];

    private static async Task AddBandAsync(
        ICollection<(MemorySearchResult Result, int Priority, int QueryIndex)> bands,
        ILocalMemoryStore memoryStore,
        string collection,
        string query,
        int limit,
        int priority,
        int queryIndex,
        IReadOnlyDictionary<string, string>? filter,
        CancellationToken cancellationToken)
    {
        var results = await memoryStore.SearchAsync(new MemorySearchRequest(collection, query, limit, filter), cancellationToken);

        foreach (var result in results)
        {
            bands.Add((WithCollectionMetadata(result, collection), priority, queryIndex));
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

    private static async Task ExpandRelatedMemoriesAsync(ICollection<(MemorySearchResult Result, int Priority, int QueryIndex)> bands, ILocalMemoryStore memoryStore, CancellationToken cancellationToken)
    {
        var seedSessions = bands
            .Select(item => new { SessionId = GetMetadata(item.Result.Metadata, "sessionId"), item.Result.Score, item.Priority, item.QueryIndex })
            .Where(item => !string.IsNullOrWhiteSpace(item.SessionId))
            .GroupBy(item => item.SessionId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Priority).ThenByDescending(item => item.Score).First())
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Score)
            .Take(2)
            .ToList();

        foreach (var seed in seedSessions)
        {
            var related = await memoryStore.InspectCollectionAsync(MemoryLayers.Memory, new MemoryCollectionInspectRequest(
                Limit: 16,
                Filter: new Dictionary<string, string> { ["sessionId"] = seed.SessionId! }), cancellationToken);

            foreach (var record in related.Records)
            {
                if (MemoryRecallPolicy.IsNegativeKnowledgeMemory(record.TextPreview))
                {
                    continue;
                }

                var metadata = record.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
                metadata.TryAdd("collection", MemoryLayers.Memory);
                bands.Add((new MemorySearchResult(record.Id, record.TextPreview, Math.Min(seed.Score, 0.55), metadata), seed.Priority + 3, seed.QueryIndex));
            }
        }
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private sealed record MemoryCollectionBand(string Name, int Priority, IReadOnlyDictionary<string, string>? Filter);

    private sealed record MemoryResetAllRequest(string Confirm);

    private sealed record MemoryResetAllResponse(int DeletedCollectionCount, IReadOnlyList<string> DeletedCollections);

    private sealed record MemoryResetEverythingRequest(string Confirm);

    private sealed record MemoryResetEverythingResponse(int DeletedCollectionCount, IReadOnlyList<string> DeletedCollections, int DeletedSqliteRows);

    private static IReadOnlyList<MemorySearchResult> OrderByIds(IReadOnlyList<MemorySearchResult> candidates, IReadOnlyList<string> ids, int limit)
    {
        var selected = ids
            .Select(id => candidates.FirstOrDefault(candidate => candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (!selected.Any(item => item.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase)))
            {
                selected.Add(candidate);
            }
        }

        return selected.Take(limit).ToList();
    }
}
