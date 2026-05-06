using System.Text.Json;
using LLLMax.Api.Services;

namespace LLLMax.Api.Approvals;

public sealed class FileApprovalStore(LocalDataPaths paths) : IApprovalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ApprovalRequest> SaveAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var stream = File.Create(GetPath(request.Id));
            await JsonSerializer.SerializeAsync(stream, request, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return request;
    }

    public async Task<ApprovalRequest?> GetAsync(string id, CancellationToken cancellationToken)
    {
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
        var approvals = new List<ApprovalRequest>();

        foreach (var file in Directory.EnumerateFiles(paths.ApprovalsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var approval = await JsonSerializer.DeserializeAsync<ApprovalRequest>(stream, JsonOptions, cancellationToken);

            if (approval is not null)
            {
                approvals.Add(approval);
            }
        }

        return approvals.OrderByDescending(approval => approval.UpdatedAt).ToList();
    }

    private string GetPath(string id) => Path.Combine(paths.ApprovalsDirectory, $"{id}.json");
}
