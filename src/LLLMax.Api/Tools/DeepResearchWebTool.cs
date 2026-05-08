using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.BackgroundJobs;

namespace LLLMax.Api.Tools;

public sealed class DeepResearchWebTool(IBackgroundJobService jobs) : LocalToolBase<DeepResearchWebArguments>
{
    public override string Name => "deep_research_web";

    public override string Description => "Schedule background web research for multi-page or long-running web synthesis. Use web_browse directly for simple current-page, headline, Reddit, or news summaries.";

    protected override async Task<LocalToolResult> InvokeAsync(DeepResearchWebArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToElement(new WebResearchJobRequest(
            Question: arguments.Question,
            Urls: arguments.Urls,
            Title: arguments.Title,
            Model: arguments.Model));
        var job = await jobs.EnqueueAsync(new BackgroundJobCreateRequest(
            Kind: BackgroundJobKinds.WebResearch,
            Payload: payload,
            Title: arguments.Title ?? "Deep web research",
            SessionId: invocation.ConversationId,
            Agent: invocation.Agent.Name,
            NotifySession: arguments.NotifySession ?? true), cancellationToken);

        return new LocalToolResult($"Scheduled deep web research job {job.Id}. Status: {job.Status}. The job will continue in the background.");
    }
}

public sealed record DeepResearchWebArguments(
    [property: Required] string Question,
    [property: Required] IReadOnlyList<string> Urls,
    string? Title = null,
    string? Model = null,
    bool? NotifySession = true);
