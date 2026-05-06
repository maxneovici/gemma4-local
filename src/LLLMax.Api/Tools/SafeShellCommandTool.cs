using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Tools;

public sealed class SafeShellCommandTool(IOptions<LocalAiOptions> options, IWebHostEnvironment environment) : LocalToolBase<SafeShellCommandArguments>
{
    private readonly LocalAiOptions _options = options.Value;

    public override string Name => "safe_shell_command";

    public override string Description => "Runs an exact allowlisted local shell command for build/test verification. Disabled by default.";

    protected override async Task<LocalToolResult> InvokeAsync(SafeShellCommandArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!_options.Tools.EnableSafeShell)
        {
            throw new InvalidOperationException("safe_shell_command is disabled. Set LocalAi:Tools:EnableSafeShell=true to opt in.");
        }

        var command = arguments.Command.Trim();

        if (!_options.Tools.AllowedShellCommands.Contains(command, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Command is not allowlisted: {command}");
        }

        var workingDirectory = ResolveWorkingDirectory(arguments.WorkingDirectory);
        var parts = SplitCommand(command);

        if (parts.Count == 0)
        {
            throw new InvalidOperationException("Command cannot be empty.");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.Tools.ShellTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = parts[0],
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in parts.Skip(1))
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

            return new LocalToolResult($"Command timed out after {_options.Tools.ShellTimeoutSeconds} seconds: {command}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var result = $"""
Command: {command}
Working directory: {workingDirectory}
Exit code: {process.ExitCode}

STDOUT:
{TrimOutput(stdout)}

STDERR:
{TrimOutput(stderr)}
""";

        return new LocalToolResult(result);
    }

    private string ResolveWorkingDirectory(string? requestedWorkingDirectory)
    {
        var baseDirectory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.Tools.ShellWorkingDirectory));
        var workingDirectory = string.IsNullOrWhiteSpace(requestedWorkingDirectory)
            ? baseDirectory
            : Path.GetFullPath(Path.Combine(baseDirectory, requestedWorkingDirectory));

        if (!workingDirectory.StartsWith(baseDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Requested working directory escapes the configured safe shell root.");
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException($"Working directory does not exist: {workingDirectory}");
        }

        return workingDirectory;
    }

    private static IReadOnlyList<string> SplitCommand(string command)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in command)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static string TrimOutput(string output) => output.Length <= 6000 ? output : string.Concat(output.AsSpan(0, 6000), "\n... truncated ...");
}

public sealed record SafeShellCommandArguments([property: Required] string Command, string? WorkingDirectory = null);
