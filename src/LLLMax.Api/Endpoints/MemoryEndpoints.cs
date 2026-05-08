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

        group.MapPost("/graph", async (MemoryGraphRequest request, ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await BuildMemoryGraphAsync(request, memoryStore, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        group.MapGet("/review", async (ILocalMemoryStore memoryStore, CancellationToken cancellationToken) =>
            Results.Ok(await BuildMemoryReviewAsync(memoryStore, cancellationToken)));

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
        await ExpandGraphAdjacentMemoriesAsync(bands, memoryStore, cancellationToken);

        var candidates = MemoryRecallPolicy.SelectTopDiverse(bands, queries, plan, Math.Max(limit * 4, limit));
        var ids = await recallPlanner.RerankAsync(request.Query, plan, candidates, limit, cancellationToken);
        return OrderByIds(candidates, ids, limit);
    }

    private static async Task<MemoryGraphResponse> BuildMemoryGraphAsync(MemoryGraphRequest request, ILocalMemoryStore memoryStore, CancellationToken cancellationToken)
    {
        var layer = NormalizeGraphLayer(request.Layer);
        var limit = Math.Clamp(request.Limit, 12, 160);
        var records = await LoadGraphRecordsAsync(memoryStore, layer, request.Query, limit, request.Filter, cancellationToken);
        var allowedTypes = (request.Types ?? [])
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(MemoryMetadata.NormalizeType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedRecords = records
            .Where(record => !MemoryRecallPolicy.IsNoiseResult(new MemorySearchResult(record.Id, record.TextPreview, 1, record.Metadata)))
            .Where(record => allowedTypes.Count == 0 || allowedTypes.Contains(GetMemoryType(record.Metadata)))
            .Take(limit)
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.FocusId))
        {
            selectedRecords = FocusGraphRecords(selectedRecords, request.FocusId).ToList();
        }

        var nodes = new List<MemoryGraphNode>();
        var edges = new List<MemoryGraphEdge>();
        var conceptWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var conceptLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var recordConcepts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in selectedRecords)
        {
            var concepts = ExtractGraphConcepts(record, layer).Take(8).ToList();
            recordConcepts[record.Id] = concepts;

            foreach (var concept in concepts)
            {
                conceptWeights[concept] = conceptWeights.GetValueOrDefault(concept) + 1;
                conceptLabels.TryAdd(concept, concept);
            }
        }

        var rankedConcepts = conceptWeights
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
        var conceptIds = rankedConcepts.ToDictionary(pair => pair.Key, pair => $"concept:{StableGraphId(pair.Key)}", StringComparer.OrdinalIgnoreCase);
        var totalConcepts = Math.Max(1, rankedConcepts.Count);

        for (var index = 0; index < rankedConcepts.Count; index++)
        {
            var (label, weight) = rankedConcepts[index];
            var radius = 31 + (index % 4) * 7 + Math.Min(22, weight * 3);
            var angle = (Math.PI * 2 * index) / totalConcepts;
            nodes.Add(new MemoryGraphNode(
                Id: conceptIds[label],
                Kind: "concept",
                Label: conceptLabels[label],
                Weight: weight,
                X: 50 + Math.Cos(angle) * radius,
                Y: 50 + Math.Sin(angle) * radius));
        }

        for (var index = 0; index < selectedRecords.Count; index++)
        {
            var record = selectedRecords[index];
            var recordNodeId = $"record:{record.Id}";
            var concepts = recordConcepts.GetValueOrDefault(record.Id) ?? [];
            var primary = concepts.FirstOrDefault(conceptIds.ContainsKey);
            var anchorIndex = primary is not null ? rankedConcepts.FindIndex(pair => pair.Key.Equals(primary, StringComparison.OrdinalIgnoreCase)) : index;
            var anchorAngle = anchorIndex >= 0 ? (Math.PI * 2 * anchorIndex) / totalConcepts : (Math.PI * 2 * index) / Math.Max(1, selectedRecords.Count);
            var radius = 9 + (index % 5) * 4;

            nodes.Add(new MemoryGraphNode(
                Id: recordNodeId,
                Kind: "record",
                Label: GraphRecordLabel(record),
                Weight: 1,
                X: 50 + Math.Cos(anchorAngle) * radius + (((index % 3) - 1) * 2.3),
                Y: 50 + Math.Sin(anchorAngle) * radius + (((index % 4) - 1.5) * 2.1),
                RecordId: record.Id,
                TextPreview: record.TextPreview,
                MemoryType: GetMemoryType(record.Metadata),
                Provenance: GetMetadata(record.Metadata, MemoryMetadata.ProvenanceKey),
                ObservedAt: GetMetadata(record.Metadata, "observedAt"),
                Metadata: record.Metadata));

            foreach (var concept in concepts.Where(conceptIds.ContainsKey).Take(4))
            {
                edges.Add(new MemoryGraphEdge(conceptIds[concept], recordNodeId, Math.Max(0.25, Math.Min(1, conceptWeights[concept] / 8.0)), "mentions"));
            }
        }

        foreach (var pair in selectedRecords.SelectMany(record => (recordConcepts.GetValueOrDefault(record.Id) ?? []).Where(conceptIds.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(left => (recordConcepts.GetValueOrDefault(record.Id) ?? []).Where(concept => !concept.Equals(left, StringComparison.OrdinalIgnoreCase) && conceptIds.ContainsKey(concept)).Select(right => OrderedPair(left, right)))).GroupBy(pair => pair, StringComparer.OrdinalIgnoreCase))
        {
            var parts = pair.Key.Split('\u001f');
            if (parts.Length == 2)
            {
                edges.Add(new MemoryGraphEdge(conceptIds[parts[0]], conceptIds[parts[1]], Math.Min(1, pair.Count() / 6.0), "co-occurs"));
            }
        }

        return new MemoryGraphResponse(layer, request.Query, selectedRecords.Count, nodes, edges.DistinctBy(edge => $"{edge.Source}|{edge.Target}|{edge.Kind}").ToList());
    }

    private static async Task<MemoryReviewResponse> BuildMemoryReviewAsync(ILocalMemoryStore memoryStore, CancellationToken cancellationToken)
    {
        var collections = new[] { MemoryLayers.Memory, MemoryLayers.Knowledge };
        var records = new List<(string Collection, MemoryCollectionRecordPreview Record)>();

        foreach (var collection in collections)
        {
            var inspected = await memoryStore.InspectCollectionAsync(collection, new MemoryCollectionInspectRequest(Limit: 160), cancellationToken);
            records.AddRange(inspected.Records.Select(record => (collection, record)));
        }

        var pending = records
            .Where(item => GetMetadata(item.Record.Metadata, MemoryMetadata.ReviewStatusKey)?.Equals("pending", StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => ToReviewItem(item.Collection, item.Record))
            .ToList();
        var duplicateGroups = records
            .Where(item => !MemoryRecallPolicy.IsNoiseResult(new MemorySearchResult(item.Record.Id, item.Record.TextPreview, 1, item.Record.Metadata)))
            .GroupBy(item => GetMetadata(item.Record.Metadata, MemoryMetadata.MergeKey), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => new MemoryDuplicateGroup(
                MergeKey: group.Key!,
                MemoryType: GetMemoryType(group.First().Record.Metadata),
                Topic: GetMetadata(group.First().Record.Metadata, "topic"),
                Records: group.Select(item => ToReviewItem(item.Collection, item.Record)).ToList()))
            .OrderByDescending(group => group.Records.Count)
            .Take(24)
            .ToList();

        return new MemoryReviewResponse(pending.Count, duplicateGroups.Count, pending, duplicateGroups);
    }

    private static MemoryReviewItem ToReviewItem(string collection, MemoryCollectionRecordPreview record) =>
        new(
            record.Id,
            collection,
            record.TextPreview,
            GetMemoryType(record.Metadata),
            GetMetadata(record.Metadata, MemoryMetadata.ProvenanceKey),
            GetMetadata(record.Metadata, MemoryMetadata.ConfidenceKey),
            GetMetadata(record.Metadata, "observedAt"),
            record.Metadata);

    private static async Task<IReadOnlyList<MemoryCollectionRecordPreview>> LoadGraphRecordsAsync(ILocalMemoryStore memoryStore, string layer, string? query, int limit, IReadOnlyDictionary<string, string>? filter, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            var searched = await memoryStore.SearchAsync(new MemorySearchRequest(layer, query, limit, filter), cancellationToken);
            return searched.Select(result => new MemoryCollectionRecordPreview(result.Id, result.Text, result.Text.Length, result.Metadata)).ToList();
        }

        var inspected = await memoryStore.InspectCollectionAsync(layer, new MemoryCollectionInspectRequest(limit, Filter: filter), cancellationToken);
        return inspected.Records;
    }

    private static IReadOnlyList<MemoryCollectionRecordPreview> FocusGraphRecords(IReadOnlyList<MemoryCollectionRecordPreview> records, string focusId)
    {
        var focus = records.FirstOrDefault(record => record.Id.Equals(focusId, StringComparison.OrdinalIgnoreCase));

        if (focus is null)
        {
            return records;
        }

        var focusConcepts = ExtractGraphConcepts(focus, GetMetadata(focus.Metadata, MemoryLayers.LayerKey) ?? MemoryLayers.Memory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return records
            .OrderByDescending(record => record.Id.Equals(focusId, StringComparison.OrdinalIgnoreCase) ? 100 : ExtractGraphConcepts(record, GetMetadata(record.Metadata, MemoryLayers.LayerKey) ?? MemoryLayers.Memory).Count(focusConcepts.Contains))
            .Take(48)
            .ToList();
    }

    private static IReadOnlyList<string> ExtractGraphConcepts(MemoryCollectionRecordPreview record, string layer)
    {
        var concepts = new List<string>();

        AddConcept(concepts, GetMetadata(record.Metadata, "category"));
        AddConcept(concepts, GetMetadata(record.Metadata, MemoryMetadata.TypeKey));
        AddConcept(concepts, GetMetadata(record.Metadata, "topic"));
        AddConcept(concepts, GetMetadata(record.Metadata, "subject"));
        AddConcept(concepts, GetMetadata(record.Metadata, "project"));
        AddConcept(concepts, GetMetadata(record.Metadata, "preference"));
        AddConcept(concepts, GetMetadata(record.Metadata, "source"));
        AddConcept(concepts, GetMetadata(record.Metadata, "sourceFile"));
        AddConcept(concepts, GetMetadata(record.Metadata, "tenant"));

        foreach (var token in ExtractKeywordConcepts(record.TextPreview))
        {
            AddConcept(concepts, token);
        }

        if (concepts.Count == 0)
        {
            concepts.Add(layer.Equals(MemoryLayers.Knowledge, StringComparison.OrdinalIgnoreCase) ? "knowledge" : "memory");
        }

        return concepts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> ExtractKeywordConcepts(string text)
    {
        var ignored = new HashSet<string>(["about", "after", "again", "also", "because", "before", "being", "could", "first", "from", "have", "into", "local", "memory", "more", "need", "needs", "only", "over", "prefer", "prefers", "should", "that", "their", "there", "these", "this", "user", "when", "with", "would"], StringComparer.OrdinalIgnoreCase);
        return text.Split([' ', '\n', '\r', '\t', ',', '.', ';', ':', '/', '\\', '(', ')', '[', ']', '{', '}', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim('-', '_').ToLowerInvariant())
            .Where(token => token.Length is >= 4 and <= 28 && token.Any(char.IsLetter) && !ignored.Contains(token))
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(group => group.Key);
    }

    private static void AddConcept(ICollection<string> concepts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var label = GraphConceptLabel(value);

            if (!IsGenericGraphConcept(label))
            {
                concepts.Add(label);
            }
        }
    }

    private static bool IsGenericGraphConcept(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "user" or "profile" or "memory" or "model memory" or "passive interaction" or "memory reflection" or "session summary" or "consolidated session" or "core memory";
    }

    private static string GraphConceptLabel(string value)
    {
        var candidate = Path.GetFileNameWithoutExtension(value.Trim());
        return string.IsNullOrWhiteSpace(candidate) ? value.Trim() : candidate.Replace('_', ' ').Replace('-', ' ');
    }

    private static string GraphRecordLabel(MemoryCollectionRecordPreview record) =>
        GetMetadata(record.Metadata, "title")
        ?? GetMetadata(record.Metadata, "topic")
        ?? NonGenericMetadata(record.Metadata, "category")
        ?? NonGenericMetadata(record.Metadata, "subject")
        ?? Truncate(record.TextPreview, 42);

    private static string GetMemoryType(IReadOnlyDictionary<string, string> metadata) =>
        MemoryMetadata.NormalizeType(GetMetadata(metadata, MemoryMetadata.TypeKey) ?? GetMetadata(metadata, "category") ?? GetMetadata(metadata, "kind") ?? "fact");

    private static string? NonGenericMetadata(IReadOnlyDictionary<string, string> metadata, string key)
    {
        var value = GetMetadata(metadata, key);
        return value is not null && !IsGenericGraphConcept(value) ? value : null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : $"{value[..Math.Max(0, maxLength - 1)]}…";

    private static string StableGraphId(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];

    private static string OrderedPair(string left, string right) =>
        string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0 ? $"{left}\u001f{right}" : $"{right}\u001f{left}";

    private static string NormalizeGraphLayer(string layer) =>
        layer.Equals(MemoryLayers.Knowledge, StringComparison.OrdinalIgnoreCase) ? MemoryLayers.Knowledge : MemoryLayers.Memory;

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

    private static async Task ExpandGraphAdjacentMemoriesAsync(ICollection<(MemorySearchResult Result, int Priority, int QueryIndex)> bands, ILocalMemoryStore memoryStore, CancellationToken cancellationToken)
    {
        var seeds = bands.OrderByDescending(item => item.Result.Score).Take(10).ToList();
        var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in seeds)
        {
            foreach (var filter in BuildAdjacencyFilters(seed.Result.Metadata))
            {
                var inspected = await memoryStore.InspectCollectionAsync(MemoryLayers.Memory, new MemoryCollectionInspectRequest(Limit: 8, Filter: filter), cancellationToken);

                foreach (var record in inspected.Records)
                {
                    if (!addedKeys.Add(record.Id))
                    {
                        continue;
                    }

                    var metadata = record.Metadata.ToDictionary(StringComparer.OrdinalIgnoreCase);
                    metadata.TryAdd("collection", MemoryLayers.Memory);
                    metadata.TryAdd("recallExpansion", "graph_adjacency");
                    bands.Add((new MemorySearchResult(record.Id, record.TextPreview, Math.Min(seed.Result.Score, 0.48), metadata), seed.Priority + 4, seed.QueryIndex));
                }
            }
        }
    }

    private static IEnumerable<IReadOnlyDictionary<string, string>> BuildAdjacencyFilters(IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var key in new[] { "topic", "category", "project", MemoryMetadata.TypeKey, MemoryMetadata.MergeKey })
        {
            if (GetMetadata(metadata, key) is { } value && !IsOverbroadAdjacency(key, value))
            {
                yield return new Dictionary<string, string> { [key] = value };
            }
        }
    }

    private static bool IsOverbroadAdjacency(string key, string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return key.Equals(MemoryMetadata.TypeKey, StringComparison.OrdinalIgnoreCase) && normalized is "fact" or "summary" or "schema"
            || key.Equals("category", StringComparison.OrdinalIgnoreCase) && normalized is "profile" or "session_summary";
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
