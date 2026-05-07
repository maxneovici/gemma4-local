using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Tools;

public sealed class GitInspectTool(IOptions<LocalAiOptions> options, IWebHostEnvironment environment) : LocalToolBase<GitInspectArguments>
{
    private readonly LocalAiOptions _options = options.Value;

    public override string Name => "git_inspect";

    public override string Description => "Read-only Git inspection for the configured local workspace. Never mutates the repository.";

    protected override async Task<LocalToolResult> InvokeAsync(GitInspectArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var operation = arguments.Operation.Trim().ToLowerInvariant();
        var gitArguments = operation switch
        {
            "status" => ["status", "--short", "--branch"],
            "diff_stat" => ["diff", "--stat"],
            "diff" => BuildDiffArguments(arguments.Path),
            "log" => ["log", "--oneline", "-8"],
            _ => throw new InvalidOperationException("Allowed git_inspect operations: status, diff_stat, diff, log.")
        };

        return new LocalToolResult(await RunGitAsync(gitArguments, cancellationToken));
    }

    private static string[] BuildDiffArguments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ["diff"];
        }

        if (Path.IsPathRooted(path) || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part == ".."))
        {
            throw new InvalidOperationException("git_inspect diff paths must be relative and cannot contain '..'.");
        }

        return ["diff", "--", path];
    }

    private async Task<string> RunGitAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.Tools.ShellTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = environment.ContentRootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return "git_inspect timed out.";
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return $"git {string.Join(' ', arguments)}\nExit code: {process.ExitCode}\n\nSTDOUT:\n{TrimOutput(stdout)}\n\nSTDERR:\n{TrimOutput(stderr)}";
    }

    private static string TrimOutput(string output) => output.Length <= 12000 ? output : string.Concat(output.AsSpan(0, 12000), "\n... truncated ...");
}

public sealed record GitInspectArguments(
    [property: Required] string Operation,
    string? Path = null);
