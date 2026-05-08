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

    public override string Description => "Fetch an HTTP or HTTPS page and return sanitized text for local reasoning. For Reddit or similar pages that return verification, login, or app walls, try the public subreddit/top or old.reddit listing URL in a follow-up web_browse call when appropriate; for example use /r/<subreddit>/top/?t=day for current top reactions.";

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

        var html = await httpClientFactory.CreateClient("web-browse").GetStringAsync(uri, cancellationToken);
        var highlights = ExtractHighlights(html);
        var text = StripHtml(html);

        var content = text.Length <= _options.WebBrowsing.MaxResponseCharacters
            ? text
            : text[.._options.WebBrowsing.MaxResponseCharacters];
        var result = highlights.Count == 0
            ? $"URL: {uri}\n\n{content}"
            : $"URL: {uri}\n\nLikely page headlines / article candidates:\n{string.Join("\n", highlights.Select(item => $"- {item}"))}\n\nPage text:\n{content}";

        return new LocalToolResult(
            $"{result}\n\n{GuidanceFor(uri, content)}",
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

    private static IReadOnlyList<string> ExtractHighlights(string html)
    {
        var candidates = new List<string>();

        candidates.AddRange(Regex.Matches(html, @"<h[1-3][^>]*>(?<text>[\s\S]*?)</h[1-3]>", RegexOptions.IgnoreCase)
            .Select(match => StripHtml(match.Groups["text"].Value)));
        candidates.AddRange(Regex.Matches(html, @"<a\b[^>]*>(?<text>[\s\S]*?)</a>", RegexOptions.IgnoreCase)
            .Select(match => StripHtml(match.Groups["text"].Value)));
        candidates.AddRange(Regex.Matches(html, "\\b(?:aria-label|title)=['\\\" ](?<text>[^'\\\">]{28,180})['\\\" ]", RegexOptions.IgnoreCase)
            .Select(match => match.Groups["text"].Value));

        return candidates
            .Select(CleanHighlight)
            .Where(item => item.Length is >= 28 and <= 150)
            .Where(IsLikelyHeadline)
            .Where(item => item.Count(char.IsLetter) >= 18)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static string CleanHighlight(string value) =>
        Regex.Replace(value, "\\s+", " ").Trim(' ', '-', '•', '|', ':');

    private static bool IsLikelyHeadline(string value)
    {
        var lower = value.ToLowerInvariant();

        if (ContainsAny(lower, "logga in", "prenumerera", "kundservice", "annons", "cookie", "hoppa till", "menu", "meny", "quiz", "korsord", "erbjudanden", "något gick fel", "använd förstorad", "läs mer", "foto:"))
        {
            return false;
        }

        return (Regex.IsMatch(value, @"\b\d{1,2}:\d{2}\b")
            || ContainsAny(lower, "just nu", "senaste", "direkt", "döms", "anklagar", "pausar", "larm", "kriget", "konflikt", "sverige", "världen", "politik", "ekonomi", "sport", "kultur")
            || char.IsUpper(value[0]))
            && value.Count(character => character == ' ') >= 3;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string GuidanceFor(Uri uri, string content)
    {
        if (!uri.Host.Contains("reddit.com", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (!content.Contains("verification", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("log in", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("app", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return "Tool guidance: Reddit returned a verification/login/app wall. If the user asked for current subreddit reactions, choose a follow-up web_browse URL such as https://www.reddit.com/r/<subreddit>/top/?t=day or https://old.reddit.com/r/<subreddit>/top/?t=day based on the subreddit in the request. If that also fails, say browsing is blocked rather than inventing reactions.";
    }
}

public sealed record WebBrowseArguments([property: Required] string Url);

file static class WebBrowseStringPipeExtensions
{
    public static string Pipe(this string value, Func<string, string> transform) => transform(value);
}
