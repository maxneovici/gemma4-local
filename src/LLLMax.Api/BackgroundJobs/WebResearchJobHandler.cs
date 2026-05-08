using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.BackgroundJobs;

public sealed class WebResearchJobHandler(
    IHttpClientFactory httpClientFactory,
    ILocalChatClient chatClient,
    IOptions<LocalAiOptions> options) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalAiOptions _options = options.Value;

    public string Kind => BackgroundJobKinds.WebResearch;

    public async Task<string> RunAsync(BackgroundJob job, IBackgroundJobContext context, CancellationToken cancellationToken)
    {
        if (!_options.WebBrowsing.Enabled)
        {
            throw new InvalidOperationException("Web browsing is disabled.");
        }

        var request = job.Payload.Deserialize<WebResearchJobRequest>(JsonOptions)
            ?? throw new ArgumentException("Web research job payload could not be deserialized.");
        var urls = request.Urls
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException("Web research payload requires question.");
        }

        if (urls.Count == 0)
        {
            throw new ArgumentException("Web research payload requires at least one HTTP or HTTPS URL.");
        }

        var pages = new List<WebResearchPage>();

        for (var index = 0; index < urls.Count; index++)
        {
            var url = urls[index];
            await context.ReportAsync(new BackgroundJobProgress(index, urls.Count + 1, $"Browsing {url}"), cancellationToken);
            var text = await httpClientFactory.CreateClient("web-browse").GetStringAsync(url, cancellationToken);
            text = StripHtml(text);
            pages.Add(new WebResearchPage(url, Truncate(text, _options.WebBrowsing.MaxResponseCharacters)));
        }

        await context.ReportAsync(new BackgroundJobProgress(urls.Count, urls.Count + 1, "Synthesizing web research answer..."), cancellationToken);
        var result = await SynthesizeAsync(request, pages, cancellationToken);
        await context.AddArtifactAsync(new BackgroundJobArtifactCreateRequest(
            Kind: "web_research",
            Title: request.Title ?? "Web research",
            Content: result,
            ContentType: "text/markdown",
            FileName: $"{job.Id}-web-research.md"), cancellationToken);
        await context.ReportAsync(new BackgroundJobProgress(urls.Count + 1, urls.Count + 1, "Web research complete.", Result: result), cancellationToken);

        return result;
    }

    private async Task<string> SynthesizeAsync(WebResearchJobRequest request, IReadOnlyList<WebResearchPage> pages, CancellationToken cancellationToken)
    {
        var sourceBuilder = new StringBuilder();

        foreach (var page in pages)
        {
            sourceBuilder.AppendLine($"URL: {page.Url}");
            sourceBuilder.AppendLine(page.Text);
            sourceBuilder.AppendLine("---");
        }

        var response = await chatClient.ChatAsync(new LocalChatRequest(
            Model: request.Model ?? _options.Models.CoordinatorModel ?? _options.DefaultModel,
            Temperature: 0.1,
            Messages:
            [
                new LocalChatMessage("system", "Answer from the provided browsed pages only. Cite the URL for every factual claim. If the pages do not contain the requested information, say so."),
                new LocalChatMessage("user", $"""
Research question: {request.Question}

Browsed pages:
{sourceBuilder}
""")
            ]), cancellationToken);

        return response.Response;
    }

    private static string StripHtml(string text) =>
        Regex.Replace(text, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Pipe(value => Regex.Replace(value, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase))
            .Pipe(value => Regex.Replace(value, "<[^>]+>", " "))
            .Pipe(value => Regex.Replace(value, "\\s+", " "))
            .Trim();

    private static string Truncate(string text, int maxCharacters) =>
        text.Length <= maxCharacters ? text : text[..maxCharacters];

    private sealed record WebResearchPage(string Url, string Text);
}

public sealed record WebResearchJobRequest(
    string Question,
    IReadOnlyList<string> Urls,
    string? Title = null,
    string? Model = null);

file static class StringPipeExtensions
{
    public static string Pipe(this string value, Func<string, string> transform) => transform(value);
}
