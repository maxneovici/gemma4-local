using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.BackgroundJobs;

namespace LLLMax.Api.Tools;

public sealed class ScheduleBackgroundJobTool(IBackgroundJobService jobs) : LocalToolBase<ScheduleBackgroundJobArguments>
{
    public override string Name => "schedule_background_job";

    public override string Description => "Schedule a long-running local background job and return immediately with a durable job id.";

    protected override async Task<LocalToolResult> InvokeAsync(ScheduleBackgroundJobArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var job = await jobs.EnqueueAsync(new BackgroundJobCreateRequest(
            Kind: arguments.Kind,
            Payload: arguments.Payload,
            Title: arguments.Title,
            SessionId: invocation.ConversationId,
            Agent: invocation.Agent.Name,
            NotifySession: arguments.NotifySession ?? true), cancellationToken);

        return new LocalToolResult($"Scheduled background job {job.Id} ({job.Kind}). Status: {job.Status}. The job will continue outside this chat turn.");
    }
}

public sealed record ScheduleBackgroundJobArguments(
    [property: Required] string Kind,
    [property: Required] JsonElement Payload,
    string? Title = null,
    bool? NotifySession = true);
