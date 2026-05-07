using System.Text.Json;
using System.Text.RegularExpressions;
using LLLMax.Api.Approvals;
using LLLMax.Api.Services;

namespace LLLMax.Api.Tools;

public sealed class CreateSkillTool(LocalDataPaths paths, IApprovalService approvals) : LocalToolBase<CreateSkillArguments>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override string Name => "create_skill";

    public override string Description => "Create a new local markdown skill under the configured skills directory after explicit human approval.";

    protected override async Task<LocalToolResult> InvokeAsync(CreateSkillArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(arguments.ApprovalId))
        {
            return await FinalizeApprovedSkillAsync(arguments.ApprovalId, cancellationToken);
        }

        var skillName = NormalizeSkillName(arguments.Name);
        var content = BuildSkillMarkdown(arguments with { Name = skillName });
        var payload = JsonSerializer.SerializeToElement(new CreateSkillApprovalPayload(skillName, content, arguments.Overwrite), JsonOptions);
        var approval = await FindApprovalAsync(payload, invocation.ConversationId, cancellationToken);

        if (approval is null)
        {
            var created = await approvals.CreateAsync(new ApprovalCreateRequest(
                Kind: "create_skill",
                Title: $"Create skill {skillName}",
                Description: "Agent requested to create or update a local markdown skill. Review the skill content before approving.",
                Payload: payload,
                ConversationId: invocation.ConversationId), cancellationToken);

            return new LocalToolResult($"Skill creation requires approval. ApprovalId={created.Id}. Ask the user to approve it, then retry create_skill with only approvalId={created.Id}.");
        }

        if (approval.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalToolResult($"Skill creation was rejected. ApprovalId={approval.Id}.");
        }

        return await WriteApprovedSkillAsync(approval, skillName, content, arguments.Overwrite, cancellationToken);
    }

    private async Task<LocalToolResult> FinalizeApprovedSkillAsync(string approvalId, CancellationToken cancellationToken)
    {
        var approval = await approvals.GetAsync(approvalId, cancellationToken)
            ?? throw new InvalidOperationException($"Approval '{approvalId}' does not exist.");

        if (!approval.Kind.Equals("create_skill", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Approval '{approvalId}' is for {approval.Kind}, not create_skill.");
        }

        if (!approval.Status.Equals("approved", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalToolResult($"Skill creation is not approved. ApprovalId={approval.Id}; Status={approval.Status}.");
        }

        var payload = approval.Payload.Deserialize<CreateSkillApprovalPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Approved skill payload could not be deserialized.");

        return await WriteApprovedSkillAsync(approval, payload.Name, payload.Content, payload.Overwrite, cancellationToken);
    }

    private async Task<LocalToolResult> WriteApprovedSkillAsync(ApprovalRequest approval, string skillName, string content, bool overwrite, CancellationToken cancellationToken)
    {
        var file = Path.Combine(paths.SkillsDirectory, $"{skillName}.md");

        if (File.Exists(file) && !overwrite)
        {
            throw new InvalidOperationException($"Skill '{skillName}' already exists and overwrite=false.");
        }

        Directory.CreateDirectory(paths.SkillsDirectory);
        await File.WriteAllTextAsync(file, content, cancellationToken);

        if (approval.Scope?.Equals(ApprovalScopes.Once, StringComparison.OrdinalIgnoreCase) != false)
        {
            await approvals.DeleteAsync(approval.Id, cancellationToken);
        }

        return new LocalToolResult($"Created skill: skills/{skillName}.md");
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

    private static string BuildSkillMarkdown(CreateSkillArguments arguments)
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
enabled: true
---
{arguments.Body.Trim()}
""";
    }

    private async Task<ApprovalRequest?> FindApprovalAsync(JsonElement payload, string? conversationId, CancellationToken cancellationToken)
    {
        var approvalsList = await approvals.ListAsync(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(payload);

        return approvalsList.FirstOrDefault(approval =>
            approval.Kind.Equals("create_skill", StringComparison.OrdinalIgnoreCase)
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

public sealed record CreateSkillArguments(
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Triggers = null,
    IReadOnlyList<string>? Agents = null,
    string? Body = null,
    int? Priority = 50,
    bool Overwrite = false,
    string? ApprovalId = null);

public sealed record CreateSkillApprovalPayload(string Name, string Content, bool Overwrite);
