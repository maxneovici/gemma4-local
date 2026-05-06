namespace LLLMax.Api.Approvals;

public interface IApprovalService
{
    Task<ApprovalRequest> CreateAsync(ApprovalCreateRequest request, CancellationToken cancellationToken);

    Task<ApprovalRequest> ApproveAsync(string id, string? reason, CancellationToken cancellationToken);

    Task<ApprovalRequest> RejectAsync(string id, string? reason, CancellationToken cancellationToken);

    Task<ApprovalRequest?> GetAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApprovalRequest>> ListAsync(CancellationToken cancellationToken);
}
