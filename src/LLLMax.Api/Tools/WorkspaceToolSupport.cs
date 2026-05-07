using LLLMax.Api.Options;

namespace LLLMax.Api.Tools;

public sealed class WorkspaceToolSupport(LocalAiOptions options, IWebHostEnvironment environment)
{
    public string ResolvePath(string relativePath)
    {
        if (!options.Tools.EnableWorkspaceTools)
        {
            throw new InvalidOperationException("Workspace tools are disabled. Set LocalAi:Tools:EnableWorkspaceTools=true to opt in.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Workspace tool paths must be relative to an allowed workspace root.");
        }

        var candidate = Path.GetFullPath(Path.Combine(environment.ContentRootPath, relativePath));
        var roots = options.Tools.WorkspaceRoots
            .Select(root => Path.GetFullPath(Path.Combine(environment.ContentRootPath, root)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToList();

        if (!roots.Any(root => (candidate + (Directory.Exists(candidate) ? Path.DirectorySeparatorChar : string.Empty)).StartsWith(root, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Requested path escapes configured LocalAi:Tools:WorkspaceRoots.");
        }

        return candidate;
    }
}
