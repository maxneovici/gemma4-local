using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using LLLMax.Api.Options;

namespace LLLMax.Api.Tools;

public sealed class WorkspaceSearchTool(WorkspaceToolSupport workspace, IOptions<LocalAiOptions> options) : LocalToolBase<WorkspaceSearchArguments>
{
    private readonly LocalAiOptions _options = options.Value;

    public override string Name => "workspace_search";

    public override string Description => "Search text files under configured local workspace roots. Read-only.";

    protected override async Task<LocalToolResult> InvokeAsync(WorkspaceSearchArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var root = workspace.ResolvePath(arguments.Path ?? ".");
        var regex = new Regex(arguments.Query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        var limit = Math.Clamp(arguments.Limit ?? 20, 1, 100);
        var results = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, arguments.Glob ?? "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (results.Count >= limit || IsSkipped(file))
            {
                continue;
            }

            string text;

            try
            {
                var info = new FileInfo(file);
                if (info.Length > _options.Tools.WorkspaceMaxReadBytes)
                {
                    continue;
                }

                text = await File.ReadAllTextAsync(file, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var lines = text.Split('\n');

            for (var index = 0; index < lines.Length && results.Count < limit; index++)
            {
                if (regex.IsMatch(lines[index]))
                {
                    results.Add($"{Path.GetRelativePath(root, file)}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        return new LocalToolResult(results.Count == 0 ? "No workspace matches found." : string.Join(Environment.NewLine, results));
    }

    private static bool IsSkipped(string file)
    {
        var parts = file.Split(Path.DirectorySeparatorChar);
        return parts.Any(part => part is ".git" or "bin" or "obj" or "node_modules");
    }
}

public sealed record WorkspaceSearchArguments(
    [property: Required] string Query,
    string? Path = null,
    string? Glob = null,
    int? Limit = null);
