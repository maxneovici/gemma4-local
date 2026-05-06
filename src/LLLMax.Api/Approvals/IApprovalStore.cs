namespace LLLMax.Api.Approvals;

public interface IApprovalStore
{
    Task<ApprovalRequest> SaveAsync(ApprovalRequest request, CancellationToken cancellationToken);

    Task<ApprovalRequest?> GetAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApprovalRequest>> ListAsync(CancellationToken cancellationToken);
}
