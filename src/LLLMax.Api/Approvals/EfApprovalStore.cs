using System.Text.Json;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.Approvals;

public sealed class EfApprovalStore(
    IDbContextFactory<LocalDbContext> dbFactory,
    LocalDataPaths paths,
    ILogger<EfApprovalStore> logger) : IApprovalStore
{
    private const string ImportMarker = "approvals_json_imported";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ApprovalRequest> _runtimeApprovals = new(StringComparer.OrdinalIgnoreCase);
    private bool _imported;

    public async Task<ApprovalRequest> SaveAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _runtimeApprovals[request.Id] = request;
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.Approvals.SingleOrDefaultAsync(approval => approval.Id == request.Id, cancellationToken);

            if (ShouldPersist(request))
            {
                if (existing is null)
                {
                    db.Approvals.Add(ToEntity(request));
                }
                else
                {
                    Copy(request, existing);
                }
            }
            else if (existing is not null)
            {
                db.Approvals.Remove(existing);
            }

            await db.SaveChangesAsync(cancellationToken);
            return request;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ApprovalRequest?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);

        if (_runtimeApprovals.TryGetValue(id, out var runtimeApproval))
        {
            return runtimeApproval;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(approval => approval.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<ApprovalRequest>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var persisted = await db.Approvals.AsNoTracking().Select(approval => ToModel(approval)).ToListAsync(cancellationToken);

        return _runtimeApprovals.Values
            .Concat(persisted)
            .GroupBy(approval => approval.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(approval => approval.UpdatedAt).First())
            .OrderByDescending(approval => approval.UpdatedAt)
            .ToList();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _runtimeApprovals.Remove(id);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Approvals.Where(approval => approval.Id == id).ExecuteDeleteAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureImportedAsync(CancellationToken cancellationToken) =>
        await JsonImport.ImportOnceAsync(dbFactory, _gate, () => _imported, () => _imported = true, ImportMarker, ImportJsonAsync, cancellationToken);

    private async Task ImportJsonAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(paths.ApprovalsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(file);
                var approval = await JsonSerializer.DeserializeAsync<ApprovalRequest>(stream, JsonOptions, cancellationToken);

                if (approval is null || !ShouldPersist(approval) || await db.Approvals.AnyAsync(existing => existing.Id == approval.Id, cancellationToken))
                {
                    continue;
                }

                db.Approvals.Add(ToEntity(approval));
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Could not import approval file {File}", file);
            }
        }
    }

    private static bool ShouldPersist(ApprovalRequest request) =>
        request.Status.Equals("approved", StringComparison.OrdinalIgnoreCase)
        && request.Scope?.Equals(ApprovalScopes.Persistent, StringComparison.OrdinalIgnoreCase) == true;

    private static ApprovalEntity ToEntity(ApprovalRequest approval)
    {
        var entity = new ApprovalEntity();
        Copy(approval, entity);
        return entity;
    }

    private static void Copy(ApprovalRequest approval, ApprovalEntity entity)
    {
        entity.Id = approval.Id;
        entity.Kind = approval.Kind;
        entity.Status = approval.Status;
        entity.Title = approval.Title;
        entity.Description = approval.Description;
        entity.PayloadJson = JsonElementValue.Serialize(approval.Payload);
        entity.CreatedAt = approval.CreatedAt;
        entity.UpdatedAt = approval.UpdatedAt;
        entity.ConversationId = approval.ConversationId;
        entity.DecisionReason = approval.DecisionReason;
        entity.Scope = approval.Scope;
    }

    private static ApprovalRequest ToModel(ApprovalEntity entity) =>
        new(
            Id: entity.Id,
            Kind: entity.Kind,
            Status: entity.Status,
            Title: entity.Title,
            Description: entity.Description,
            Payload: JsonElementValue.Parse(entity.PayloadJson),
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt,
            ConversationId: entity.ConversationId,
            DecisionReason: entity.DecisionReason,
            Scope: entity.Scope);
}
