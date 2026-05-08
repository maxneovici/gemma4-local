using System.Text.Json;
using LLLMax.Api.Memory;

namespace LLLMax.Api.BackgroundJobs;

public sealed class MemoryConsolidationJobHandler(IMemoryConsolidationService consolidation) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => BackgroundJobKinds.MemoryConsolidation;

    public async Task<string> RunAsync(BackgroundJob job, IBackgroundJobContext context, CancellationToken cancellationToken)
    {
        var request = job.Payload.Deserialize<MemoryConsolidationRequest>(JsonOptions)
            ?? throw new ArgumentException("Memory consolidation job payload could not be deserialized.");

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("Memory consolidation payload requires sessionId.");
        }

        await context.ReportAsync(new BackgroundJobProgress(0, 2, $"Consolidating session {request.SessionId} into memory..."), cancellationToken);
        var response = await consolidation.ConsolidateSessionAsync(request, cancellationToken);
        var result = response.Job.Summary ?? $"Memory consolidation completed for session {request.SessionId}.";

        await context.ReportAsync(new BackgroundJobProgress(2, 2, "Memory consolidation complete.", Result: result), cancellationToken);
        return result;
    }
}
