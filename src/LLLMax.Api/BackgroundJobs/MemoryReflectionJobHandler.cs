using System.Text.Json;
using LLLMax.Api.Memory;

namespace LLLMax.Api.BackgroundJobs;

public sealed class MemoryReflectionJobHandler(IMemoryReflectionService reflection) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => BackgroundJobKinds.MemoryReflection;

    public async Task<string> RunAsync(BackgroundJob job, IBackgroundJobContext context, CancellationToken cancellationToken)
    {
        var request = job.Payload.ValueKind == JsonValueKind.Null || job.Payload.ValueKind == JsonValueKind.Undefined
            ? new MemoryReflectionRequest()
            : job.Payload.Deserialize<MemoryReflectionRequest>(JsonOptions) ?? new MemoryReflectionRequest();

        await context.ReportAsync(new BackgroundJobProgress(0, 2, "Reflecting profile memories into canonical memory..."), cancellationToken);
        var response = await reflection.ReflectAsync(request, cancellationToken);
        var result = $"Memory reflection complete. Read {response.SourceRecordCount} profile memories, wrote {response.FactsWritten} canonical facts and {response.CategoriesWritten} categories. {response.Summary}";
        await context.ReportAsync(new BackgroundJobProgress(2, 2, "Memory reflection complete.", Result: result), cancellationToken);
        return result;
    }
}
