using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.Approvals;

namespace LLLMax.Api.Tools;

public sealed class WorkspaceWriteTool(WorkspaceToolSupport workspace, IApprovalService approvals) : LocalToolBase<WorkspaceWriteArguments>
{
    public override string Name => "workspace_write";

    public override string Description => "Write a local text file under configured workspace roots after explicit human approval.";

    protected override async Task<LocalToolResult> InvokeAsync(WorkspaceWriteArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var path = workspace.ResolvePath(arguments.Path);
        var payload = JsonSerializer.SerializeToElement(new WorkspaceWriteApprovalPayload(arguments.Path, arguments.Content, arguments.Overwrite));
        var approval = await FindApprovalAsync(payload, invocation.ConversationId, cancellationToken);

        if (approval is null)
        {
            var created = await approvals.CreateAsync(new ApprovalCreateRequest(
                Kind: "workspace_write",
                Title: $"Write workspace file {arguments.Path}",
                Description: $"Agent '{invocation.Agent.Name}' requested to write a local workspace file. Approve only if the proposed content is expected.",
                Payload: payload,
                ConversationId: invocation.ConversationId), cancellationToken);

            return new LocalToolResult($"Workspace write requires approval. ApprovalId={created.Id}. Ask the user to approve it in the approval queue, then retry.");
        }

        if (approval.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalToolResult($"Workspace write was rejected. ApprovalId={approval.Id}.");
        }

        if (File.Exists(path) && !arguments.Overwrite)
        {
            throw new InvalidOperationException($"Workspace file already exists and overwrite=false: {arguments.Path}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Invalid workspace file path."));
        await File.WriteAllTextAsync(path, arguments.Content, cancellationToken);

        if (approval.Scope?.Equals(ApprovalScopes.Once, StringComparison.OrdinalIgnoreCase) != false)
        {
            await approvals.DeleteAsync(approval.Id, cancellationToken);
        }

        return new LocalToolResult($"Wrote workspace file: {arguments.Path}");
    }

    private async Task<ApprovalRequest?> FindApprovalAsync(JsonElement payload, string? conversationId, CancellationToken cancellationToken)
    {
        var approvalsList = await approvals.ListAsync(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(payload);

        return approvalsList.FirstOrDefault(approval =>
            approval.Kind.Equals("workspace_write", StringComparison.OrdinalIgnoreCase)
            && approval.Status is "approved" or "rejected"
            && JsonSerializer.Serialize(approval.Payload) == payloadJson
            && (approval.Scope != ApprovalScopes.Session || string.Equals(approval.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed record WorkspaceWriteArguments(
    [property: Required] string Path,
    [property: Required] string Content,
    bool Overwrite = false);

public sealed record WorkspaceWriteApprovalPayload(string Path, string Content, bool Overwrite);
