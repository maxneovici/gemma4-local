using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using LLLMax.Api.Agents;
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

        if (invocation.OnEvent is not null)
        {
            await invocation.OnEvent(new AgentRuntimeEvent(
                Kind: "tool_progress",
                Content: $"Browsing {uri}",
                Tool: Name,
                Arguments: new Dictionary<string, string> { ["url"] = uri.ToString() }), cancellationToken);
        }

        var text = await httpClientFactory.CreateClient("web-browse").GetStringAsync(uri, cancellationToken);
        text = StripHtml(text);

        var content = text.Length <= _options.WebBrowsing.MaxResponseCharacters
            ? text
            : text[.._options.WebBrowsing.MaxResponseCharacters];

        return new LocalToolResult(
            $"URL: {uri}\n\n{content}",
            [new CitationSource("web", uri.Host, Url: uri.ToString(), Source: uri.ToString())]);
    }

    private static string StripHtml(string text) =>
        Regex.Replace(text, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Pipe(value => Regex.Replace(value, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase))
            .Pipe(value => Regex.Replace(value, "<[^>]+>", " "))
            .Pipe(value => Regex.Replace(value, "\\s+", " "))
            .Trim();
}

public sealed record WebBrowseArguments([property: Required] string Url);

file static class WebBrowseStringPipeExtensions
{
    public static string Pipe(this string value, Func<string, string> transform) => transform(value);
}
