using System.Text;
using System.Text.Json;
using LLLMax.Api.Memory;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.BackgroundJobs;

public sealed class MemoryReportJobHandler(
    ILocalMemoryStore memoryStore,
    ILocalChatClient chatClient,
    IOptions<LocalAiOptions> options) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalAiOptions _options = options.Value;

    public string Kind => BackgroundJobKinds.MemoryReport;

    public async Task<string> RunAsync(BackgroundJob job, IBackgroundJobContext context, CancellationToken cancellationToken)
    {
        var request = job.Payload.Deserialize<MemoryReportJobRequest>(JsonOptions)
            ?? throw new ArgumentException("Memory report job payload could not be deserialized.");

        if (string.IsNullOrWhiteSpace(request.Collection))
        {
            throw new ArgumentException("Memory report payload requires collection.");
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("Memory report payload requires query.");
        }

        await context.ReportAsync(new BackgroundJobProgress(0, 3, $"Searching collection {request.Collection}..."), cancellationToken);
        var filter = BuildFilter(request);
        var results = await memoryStore.SearchAsync(new MemorySearchRequest(
            Collection: request.Collection,
            Query: request.Query,
            Limit: Math.Clamp(request.Limit ?? 8, 1, 25),
            Filter: filter), cancellationToken);

        if (results.Count == 0)
        {
            var empty = $"No matching memories found in `{request.Collection}` for report query `{request.Query}`.";
            await context.ReportAsync(new BackgroundJobProgress(3, 3, empty, Result: empty), cancellationToken);
            return empty;
        }

        await context.ReportAsync(new BackgroundJobProgress(1, 3, $"Retrieved {results.Count} chunks. Generating report..."), cancellationToken);
        var report = await GenerateReportAsync(request, filter, results, cancellationToken);
        await context.AddArtifactAsync(new BackgroundJobArtifactCreateRequest(
            Kind: "report",
            Title: request.Title ?? "Memory report",
            Content: report,
            ContentType: "text/markdown",
            FileName: $"{job.Id}-report.md"), cancellationToken);
        await context.ReportAsync(new BackgroundJobProgress(3, 3, "Report generated.", Result: report), cancellationToken);

        return report;
    }

    private async Task<string> GenerateReportAsync(MemoryReportJobRequest request, IReadOnlyDictionary<string, string> filter, IReadOnlyList<MemorySearchResult> results, CancellationToken cancellationToken)
    {
        var contextBuilder = new StringBuilder();

        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            contextBuilder.AppendLine($"Source {index + 1} | score={result.Score:0.000}");

            foreach (var item in result.Metadata.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                contextBuilder.AppendLine($"metadata.{item.Key}: {item.Value}");
            }

            contextBuilder.AppendLine(result.Text);
            contextBuilder.AppendLine("---");
        }

        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: request.Model ?? _options.ModelRouter.BalancedModel ?? _options.DefaultModel,
            Temperature: 0.1,
            Messages:
            [
                new LocalChatMessage("system", "You generate concise markdown reports from retrieved local memory. Use only the supplied sources. If evidence is missing, say so. Include source filenames or metadata when available."),
                new LocalChatMessage("user", $"""
Report title: {request.Title ?? "Local Memory Report"}
Report instruction: {request.Instruction ?? "Summarize the important findings and cite source metadata."}
Collection: {request.Collection}
Query: {request.Query}
Filter: {JsonSerializer.Serialize(filter, JsonOptions)}

Retrieved sources:
{contextBuilder}
""")
            ]), cancellationToken);

        return response.Response;
    }

    private static IReadOnlyDictionary<string, string> BuildFilter(MemoryReportJobRequest request)
    {
        var filter = new Dictionary<string, string>(request.Filter ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Tenant))
        {
            filter["tenant"] = request.Tenant.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            filter["category"] = request.Category.Trim();
        }

        return filter;
    }
}

public sealed record MemoryReportJobRequest(
    string Collection,
    string Query,
    string? Title = null,
    string? Instruction = null,
    int? Limit = null,
    IReadOnlyDictionary<string, string>? Filter = null,
    string? Tenant = null,
    string? Category = null,
    string? Model = null);
