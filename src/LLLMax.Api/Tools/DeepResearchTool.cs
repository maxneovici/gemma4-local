using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.BackgroundJobs;

namespace LLLMax.Api.Tools;

public sealed class DeepResearchTool(IBackgroundJobService jobs) : LocalToolBase<DeepResearchArguments>
{
    public override string Name => "deep_research";

    public override string Description => "Schedule background research over an existing local memory collection. Vectorize folders first when needed, then use this for longer report-style synthesis.";

    protected override async Task<LocalToolResult> InvokeAsync(DeepResearchArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToElement(new MemoryReportJobRequest(
            Collection: arguments.Collection,
            Query: arguments.Query,
            Title: arguments.Title,
            Instruction: arguments.Instruction,
            Limit: arguments.Limit,
            Filter: arguments.Filter,
            Tenant: arguments.Tenant,
            Category: arguments.Category,
            Model: arguments.Model));
        var job = await jobs.EnqueueAsync(new BackgroundJobCreateRequest(
            Kind: BackgroundJobKinds.MemoryReport,
            Payload: payload,
            Title: arguments.Title ?? "Deep local research",
            SessionId: invocation.ConversationId,
            Agent: invocation.Agent.Name,
            NotifySession: arguments.NotifySession ?? true), cancellationToken);

        return new LocalToolResult($"Scheduled deep local research job {job.Id}. Status: {job.Status}. The job will continue in the background.");
    }
}

public sealed record DeepResearchArguments(
    [property: Required] string Collection,
    [property: Required] string Query,
    string? Title = null,
    string? Instruction = null,
    int? Limit = null,
    IReadOnlyDictionary<string, string>? Filter = null,
    string? Tenant = null,
    string? Category = null,
    string? Model = null,
    bool? NotifySession = true);
