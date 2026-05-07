using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using LLLMax.Api.Options;

namespace LLLMax.Api.Tools;

public sealed class WorkspaceReadTool(WorkspaceToolSupport workspace, IOptions<LocalAiOptions> options) : LocalToolBase<WorkspaceReadArguments>
{
    private readonly LocalAiOptions _options = options.Value;

    public override string Name => "workspace_read";

    public override string Description => "Read a local text file under configured workspace roots. Read-only.";

    protected override async Task<LocalToolResult> InvokeAsync(WorkspaceReadArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var path = workspace.ResolvePath(arguments.Path);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Workspace file does not exist: {arguments.Path}");
        }

        var info = new FileInfo(path);
        if (info.Length > _options.Tools.WorkspaceMaxReadBytes)
        {
            throw new InvalidOperationException($"Workspace file exceeds LocalAi:Tools:WorkspaceMaxReadBytes ({_options.Tools.WorkspaceMaxReadBytes}).");
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return new LocalToolResult($"File: {arguments.Path}\n\n{text}");
    }
}

public sealed record WorkspaceReadArguments([property: Required] string Path);
