using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.Approvals;
using LLLMax.Api.Services;

namespace LLLMax.Api.Tools;

public sealed class PatchProposalTool(LocalDataPaths paths, IApprovalService approvals) : LocalToolBase<PatchProposalArguments>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override string Name => "propose_patch";

    public override string Description => "Create a reviewable patch proposal artifact after human approval. Does not apply changes or run Git.";

    protected override async Task<LocalToolResult> InvokeAsync(PatchProposalArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        Validate(arguments);
        var payload = JsonSerializer.SerializeToElement(new PatchProposalApprovalPayload(arguments.Title, arguments.Rationale, arguments.Patch), JsonOptions);
        var approval = await FindApprovalAsync(payload, invocation.ConversationId, cancellationToken);

        if (approval is null)
        {
            var created = await approvals.CreateAsync(new ApprovalCreateRequest(
                Kind: "propose_patch",
                Title: $"Create patch proposal: {arguments.Title}",
                Description: "Agent requested to save a reviewable patch proposal. This does not apply changes, but the patch content should still be reviewed.",
                Payload: payload,
                ConversationId: invocation.ConversationId), cancellationToken);

            return new LocalToolResult($"Patch proposal requires approval. ApprovalId={created.Id}. Ask the user to approve it, then retry.");
        }

        if (approval.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalToolResult($"Patch proposal was rejected. ApprovalId={approval.Id}.");
        }

        var directory = paths.PatchProposalsDirectory;
        Directory.CreateDirectory(directory);
        var safeTitle = string.Join('-', arguments.Title.ToLowerInvariant().Split(Path.GetInvalidFileNameChars().Concat([' ', '/', '\\', ':']).ToArray(), StringSplitOptions.RemoveEmptyEntries));
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}-{safeTitle}.patch";
        var path = Path.Combine(directory, fileName);
        var content = $"# {arguments.Title}\n\n{arguments.Rationale}\n\n```diff\n{arguments.Patch}\n```\n";
        await File.WriteAllTextAsync(path, content, cancellationToken);

        if (approval.Scope?.Equals(ApprovalScopes.Once, StringComparison.OrdinalIgnoreCase) != false)
        {
            await approvals.DeleteAsync(approval.Id, cancellationToken);
        }

        return new LocalToolResult($"Saved patch proposal for review: {Path.GetRelativePath(paths.Root, path)}");
    }

    private static void Validate(PatchProposalArguments arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments.Title) || string.IsNullOrWhiteSpace(arguments.Rationale) || string.IsNullOrWhiteSpace(arguments.Patch))
        {
            throw new InvalidOperationException("Title, rationale, and patch are required.");
        }

        if (!arguments.Patch.Contains("*** Begin Patch", StringComparison.Ordinal) && !arguments.Patch.Contains("diff --git", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Patch must be a recognizable apply_patch or git diff patch.");
        }
    }

    private async Task<ApprovalRequest?> FindApprovalAsync(JsonElement payload, string? conversationId, CancellationToken cancellationToken)
    {
        var approvalsList = await approvals.ListAsync(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(payload);

        return approvalsList.FirstOrDefault(approval =>
            approval.Kind.Equals("propose_patch", StringComparison.OrdinalIgnoreCase)
            && approval.Status is "approved" or "rejected"
            && JsonSerializer.Serialize(approval.Payload) == payloadJson
            && (approval.Scope != ApprovalScopes.Session || string.Equals(approval.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed record PatchProposalArguments(
    [property: Required] string Title,
    [property: Required] string Rationale,
    [property: Required] string Patch);

public sealed record PatchProposalApprovalPayload(string Title, string Rationale, string Patch);
