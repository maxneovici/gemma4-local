using System.Diagnostics;
using Gemma4Local.Api.Options;
using Microsoft.Extensions.Options;

namespace Gemma4Local.Api.Services;

public sealed class OllamaProcessHostedService(
    IOllamaApi ollamaApi,
    IOptions<LocalAiOptions> options,
    LocalEndpointGuard endpointGuard,
    ILogger<OllamaProcessHostedService> logger) : IHostedService, IDisposable
{
    private readonly LocalAiOptions _options = options.Value;
    private readonly CancellationTokenSource _logCancellation = new();
    private Process? _process;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        endpointGuard.ThrowIfRemoteEndpoint();

        if (!_options.ManageProcess)
        {
            logger.LogInformation("LocalAi:ManageProcess is false; expecting Ollama to already be running at {BaseUrl}.", _options.BaseUrl);
            return;
        }

        if (await ollamaApi.IsHealthyAsync(cancellationToken))
        {
            logger.LogInformation("Ollama is already running at {BaseUrl}; not starting a second process.", _options.BaseUrl);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.OllamaExecutable,
            Arguments = "serve",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var baseUri = new Uri(_options.BaseUrl);
        startInfo.Environment["OLLAMA_HOST"] = baseUri.IsDefaultPort ? baseUri.Host : $"{baseUri.Host}:{baseUri.Port}";
        startInfo.Environment.TryAdd("OLLAMA_FLASH_ATTENTION", "1");
        startInfo.Environment.TryAdd("OLLAMA_KV_CACHE_TYPE", "q8_0");

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Ollama process.");

        _ = DrainLogsAsync(_process.StandardOutput, LogLevel.Information, _logCancellation.Token);
        _ = DrainLogsAsync(_process.StandardError, LogLevel.Warning, _logCancellation.Token);

        logger.LogInformation("Started managed Ollama process {ProcessId} at {BaseUrl}.", _process.Id, _options.BaseUrl);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.StartupTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        while (!linked.IsCancellationRequested)
        {
            if (await ollamaApi.IsHealthyAsync(linked.Token))
            {
                logger.LogInformation("Ollama is ready at {BaseUrl}.", _options.BaseUrl);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), linked.Token);
        }

        throw new TimeoutException($"Ollama did not become ready within {_options.StartupTimeoutSeconds} seconds.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logCancellation.Cancel();

        if (_process is not { HasExited: false })
        {
            return Task.CompletedTask;
        }

        if (!_options.StopManagedProcessOnShutdown)
        {
            logger.LogInformation("Leaving managed Ollama process running because LocalAi:StopManagedProcessOnShutdown is false.");
            return Task.CompletedTask;
        }

        logger.LogInformation("Stopping managed Ollama process {ProcessId}.", _process.Id);
        _process.Kill(entireProcessTree: true);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _logCancellation.Dispose();
        _process?.Dispose();
    }

    private async Task DrainLogsAsync(StreamReader reader, LogLevel logLevel, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                logger.Log(logLevel, "ollama: {Line}", line);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
