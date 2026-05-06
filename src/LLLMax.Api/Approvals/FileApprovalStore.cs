using System.Text.Json;
using LLLMax.Api.Services;

namespace LLLMax.Api.Approvals;

public sealed class FileApprovalStore : IApprovalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalDataPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ApprovalRequest> _runtimeApprovals = new(StringComparer.OrdinalIgnoreCase);

    public FileApprovalStore(LocalDataPaths paths)
    {
        _paths = paths;
        PurgeUnpersistedApprovals();
    }

    public async Task<ApprovalRequest> SaveAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _runtimeApprovals[request.Id] = request;

            if (ShouldPersist(request))
            {
                await using var stream = File.Create(GetPath(request.Id));
                await JsonSerializer.SerializeAsync(stream, request, JsonOptions, cancellationToken);
            }
            else
            {
                DeleteFileIfExists(request.Id);
            }
        }
        finally
        {
            _gate.Release();
        }

        return request;
    }

    public async Task<ApprovalRequest?> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (_runtimeApprovals.TryGetValue(id, out var runtimeApproval))
        {
            return runtimeApproval;
        }

        var path = GetPath(id);

        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ApprovalRequest>(stream, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovalRequest>> ListAsync(CancellationToken cancellationToken)
    {
        var approvals = new List<ApprovalRequest>(_runtimeApprovals.Values);

        foreach (var file in Directory.EnumerateFiles(_paths.ApprovalsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var approval = await JsonSerializer.DeserializeAsync<ApprovalRequest>(stream, JsonOptions, cancellationToken);

            if (approval is not null)
            {
                if (!ShouldPersist(approval))
                {
                    File.Delete(file);
                    continue;
                }

                _runtimeApprovals.TryAdd(approval.Id, approval);
                approvals.Add(approval);
            }
        }

        return approvals
            .GroupBy(approval => approval.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(approval => approval.UpdatedAt).First())
            .OrderByDescending(approval => approval.UpdatedAt)
            .ToList();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _runtimeApprovals.Remove(id);
            DeleteFileIfExists(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetPath(string id) => Path.Combine(_paths.ApprovalsDirectory, $"{id}.json");

    private static bool ShouldPersist(ApprovalRequest request) =>
        request.Status.Equals("approved", StringComparison.OrdinalIgnoreCase)
        && request.Scope?.Equals(ApprovalScopes.Persistent, StringComparison.OrdinalIgnoreCase) == true;

    private void DeleteFileIfExists(string id)
    {
        var path = GetPath(id);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void PurgeUnpersistedApprovals()
    {
        foreach (var file in Directory.EnumerateFiles(_paths.ApprovalsDirectory, "*.json"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var approval = JsonSerializer.Deserialize<ApprovalRequest>(stream, JsonOptions);

                if (approval is null || !ShouldPersist(approval))
                {
                    File.Delete(file);
                }
            }
            catch (JsonException)
            {
                File.Delete(file);
            }
        }
    }
}
