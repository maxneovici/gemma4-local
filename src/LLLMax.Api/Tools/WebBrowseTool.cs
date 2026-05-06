using System.ComponentModel.DataAnnotations;
using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Tools;

public sealed class WebBrowseTool(IHttpClientFactory httpClientFactory, IOptions<LocalAiOptions> options) : LocalToolBase<WebBrowseArguments>
{
    private readonly LocalAiOptions _options = options.Value;

    public override string Name => "web_browse";

    public override string Description => "Fetch an HTTP or HTTPS page and return sanitized text for local reasoning.";

    protected override async Task<LocalToolResult> InvokeAsync(WebBrowseArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!_options.WebBrowsing.Enabled)
        {
            throw new InvalidOperationException("Web browsing is disabled.");
        }

        if (!Uri.TryCreate(arguments.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Tool argument 'url' must be an absolute HTTP or HTTPS URL.");
        }

        var text = await httpClientFactory.CreateClient("web-browse").GetStringAsync(uri, cancellationToken);
        text = StripHtml(text);

        return new LocalToolResult(text.Length <= _options.WebBrowsing.MaxResponseCharacters
            ? text
            : text[.._options.WebBrowsing.MaxResponseCharacters]);
    }

    private static string StripHtml(string text) =>
        text.Replace("<script", "\n<script", StringComparison.OrdinalIgnoreCase)
            .Replace("<style", "\n<style", StringComparison.OrdinalIgnoreCase)
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("<script", StringComparison.OrdinalIgnoreCase) && !line.TrimStart().StartsWith("<style", StringComparison.OrdinalIgnoreCase))
            .Aggregate(string.Empty, (current, line) => current + "\n" + System.Text.RegularExpressions.Regex.Replace(line, "<[^>]+>", " "))
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Trim();
}

public sealed record WebBrowseArguments([property: Required] string Url);
