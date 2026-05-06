namespace LLLMax.Api.Approvals;

public sealed class ApprovalService(IApprovalStore store) : IApprovalService
{
    public async Task<ApprovalRequest> CreateAsync(ApprovalCreateRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var approval = new ApprovalRequest(
            Id: Guid.NewGuid().ToString("n"),
            Kind: request.Kind,
            Status: "pending",
            Title: request.Title,
            Description: request.Description,
            Payload: request.Payload,
            CreatedAt: now,
            UpdatedAt: now,
            ConversationId: request.ConversationId);

        return await store.SaveAsync(approval, cancellationToken);
    }

    public async Task<ApprovalRequest> ApproveAsync(string id, string? reason, string? scope, CancellationToken cancellationToken) =>
        await DecideAsync(id, "approved", reason, NormalizeScope(scope), cancellationToken);

    public async Task<ApprovalRequest> RejectAsync(string id, string? reason, CancellationToken cancellationToken) =>
        await DecideAsync(id, "rejected", reason, null, cancellationToken);

    public Task<ApprovalRequest?> GetAsync(string id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<ApprovalRequest>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task DeleteAsync(string id, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    private async Task<ApprovalRequest> DecideAsync(string id, string status, string? reason, string? scope, CancellationToken cancellationToken)
    {
        var approval = await store.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Approval '{id}' does not exist.");

        if (!approval.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
        {
            return approval;
        }

        return await store.SaveAsync(approval with
        {
            Status = status,
            DecisionReason = reason,
            Scope = scope,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    private static string NormalizeScope(string? scope) =>
        string.IsNullOrWhiteSpace(scope)
            ? ApprovalScopes.Once
            : scope.Trim().ToLowerInvariant() switch
            {
                ApprovalScopes.Session => ApprovalScopes.Session,
                ApprovalScopes.Persistent => ApprovalScopes.Persistent,
                _ => ApprovalScopes.Once
            };
}
