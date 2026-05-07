using System.Text.Json;
using System.Text.RegularExpressions;
using LLLMax.Api.Approvals;
using LLLMax.Api.Services;

namespace LLLMax.Api.Tools;

public sealed class UpdateSkillTool(LocalDataPaths paths, IApprovalService approvals) : LocalToolBase<UpdateSkillArguments>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override string Name => "update_skill";

    public override string Description => "Update an existing local markdown skill after explicit human approval.";

    protected override async Task<LocalToolResult> InvokeAsync(UpdateSkillArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(arguments.ApprovalId))
        {
            return await FinalizeApprovedUpdateAsync(arguments.ApprovalId, cancellationToken);
        }

        var skillName = NormalizeSkillName(arguments.Name);
        var file = Path.Combine(paths.SkillsDirectory, $"{skillName}.md");

        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"Skill '{skillName}' does not exist. Use create_skill for new skills.");
        }

        var currentContent = await File.ReadAllTextAsync(file, cancellationToken);
        var newContent = BuildSkillMarkdown(arguments with { Name = skillName });
        var payload = JsonSerializer.SerializeToElement(new UpdateSkillApprovalPayload(skillName, currentContent, newContent), JsonOptions);
        var approval = await FindApprovalAsync(payload, invocation.ConversationId, cancellationToken);

        if (approval is null)
        {
            var created = await approvals.CreateAsync(new ApprovalCreateRequest(
                Kind: "update_skill",
                Title: $"Update skill {skillName}",
                Description: "Agent requested to modify an existing local markdown skill. Review the diff-equivalent old and new content before approving.",
                Payload: payload,
                ConversationId: invocation.ConversationId), cancellationToken);

            return new LocalToolResult($"Skill update requires approval. ApprovalId={created.Id}. Ask the user to approve it, then retry update_skill with only approvalId={created.Id}.");
        }

        if (approval.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalToolResult($"Skill update was rejected. ApprovalId={approval.Id}.");
        }

        return await WriteApprovedUpdateAsync(approval, skillName, newContent, cancellationToken);
    }

    private async Task<LocalToolResult> FinalizeApprovedUpdateAsync(string approvalId, CancellationToken cancellationToken)
    {
        var approval = await approvals.GetAsync(approvalId, cancellationToken)
            ?? throw new InvalidOperationException($"Approval '{approvalId}' does not exist.");

        if (!approval.Kind.Equals("update_skill", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Approval '{approvalId}' is for {approval.Kind}, not update_skill.");
        }

        if (!approval.Status.Equals("approved", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalToolResult($"Skill update is not approved. ApprovalId={approval.Id}; Status={approval.Status}.");
        }

        var payload = approval.Payload.Deserialize<UpdateSkillApprovalPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Approved skill update payload could not be deserialized.");

        return await WriteApprovedUpdateAsync(approval, payload.Name, payload.NewContent, cancellationToken);
    }

    private async Task<LocalToolResult> WriteApprovedUpdateAsync(ApprovalRequest approval, string skillName, string newContent, CancellationToken cancellationToken)
    {
        var file = Path.Combine(paths.SkillsDirectory, $"{skillName}.md");
        await File.WriteAllTextAsync(file, newContent, cancellationToken);

        if (approval.Scope?.Equals(ApprovalScopes.Once, StringComparison.OrdinalIgnoreCase) != false)
        {
            await approvals.DeleteAsync(approval.Id, cancellationToken);
        }

        return new LocalToolResult($"Updated skill: skills/{skillName}.md");
    }

    private static string NormalizeSkillName(string? name)
    {
        var normalized = Regex.Replace((name ?? string.Empty).Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Skill name must contain letters or numbers.");
        }

        return normalized;
    }

    private static string BuildSkillMarkdown(UpdateSkillArguments arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments.Description) || string.IsNullOrWhiteSpace(arguments.Body))
        {
            throw new InvalidOperationException("Description and body are required.");
        }

        var triggers = CleanList(arguments.Triggers ?? [], "triggers");
        var agents = CleanList(arguments.Agents ?? [], "agents");
        var priority = Math.Clamp(arguments.Priority ?? 50, 0, 100);

        return $"""
---
name: {arguments.Name}
description: {OneLine(arguments.Description)}
triggers:
{YamlList(triggers)}
agents:
{YamlList(agents)}
priority: {priority}
enabled: {arguments.Enabled.ToString().ToLowerInvariant()}
---
{arguments.Body.Trim()}
""";
    }

    private async Task<ApprovalRequest?> FindApprovalAsync(JsonElement payload, string? conversationId, CancellationToken cancellationToken)
    {
        var approvalsList = await approvals.ListAsync(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(payload);

        return approvalsList.FirstOrDefault(approval =>
            approval.Kind.Equals("update_skill", StringComparison.OrdinalIgnoreCase)
            && approval.Status is "approved" or "rejected"
            && JsonSerializer.Serialize(approval.Payload) == payloadJson
            && (approval.Scope != ApprovalScopes.Session || string.Equals(approval.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string> values, string field)
    {
        var clean = values.Select(OneLine).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (clean.Count == 0)
        {
            throw new InvalidOperationException($"At least one {field} item is required.");
        }

        return clean;
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string YamlList(IEnumerable<string> values) => string.Join(Environment.NewLine, values.Select(value => $"  - {value}"));
}

public sealed record UpdateSkillArguments(
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Triggers = null,
    IReadOnlyList<string>? Agents = null,
    string? Body = null,
    int? Priority = 50,
    bool Enabled = true,
    string? ApprovalId = null);

public sealed record UpdateSkillApprovalPayload(string Name, string CurrentContent, string NewContent);
