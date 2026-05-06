using System.Collections.Concurrent;
using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Services;

public sealed class LocalModelSetupService(
    IOllamaApi ollamaApi,
    IOptions<LocalAiOptions> options,
    ILogger<LocalModelSetupService> logger) : ILocalModelSetupService
{
    private readonly LocalAiOptions _options = options.Value;
    private readonly ConcurrentQueue<string> _events = new();
    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private readonly Dictionary<string, LocalModelStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _isRunning;
    private volatile bool _isComplete;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _completedAt;

    public LocalModelSetupSnapshot GetSnapshot()
    {
        lock (_status)
        {
            return new LocalModelSetupSnapshot(
                IsRunning: _isRunning,
                IsComplete: _isComplete,
                StartedAt: _startedAt,
                CompletedAt: _completedAt,
                Models: _status.Values.OrderBy(model => model.Name).ToList(),
                Events: _events.ToArray());
        }
    }

    public async Task EnsureStartupModelsAsync(CancellationToken cancellationToken)
    {
        await _setupLock.WaitAsync(cancellationToken);

        try
        {
            _isRunning = true;
            _isComplete = false;
            _startedAt = DateTimeOffset.UtcNow;
            _completedAt = null;

            await RefreshStatusAsync(cancellationToken);

            var modelsToPull = _options.RequiredModels
                .Where(model => model.PullOnStartup || (_options.EnsureDefaultModel && string.Equals(model.Name, _options.DefaultModel, StringComparison.OrdinalIgnoreCase)))
                .Select(model => model.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var model in modelsToPull)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsInstalled(model))
                {
                    AddEvent($"Model {model} already installed.");
                    continue;
                }

                await PullModelCoreAsync(model, cancellationToken);
            }

            await RefreshStatusAsync(cancellationToken);
            _isComplete = true;
            _completedAt = DateTimeOffset.UtcNow;
            AddEvent("Local model setup complete.");
        }
        finally
        {
            _isRunning = false;
            _setupLock.Release();
        }
    }

    public async Task PullModelAsync(string model, CancellationToken cancellationToken)
    {
        await _setupLock.WaitAsync(cancellationToken);

        try
        {
            _isRunning = true;
            await RefreshStatusAsync(cancellationToken);
            await PullModelCoreAsync(model, cancellationToken);
            await RefreshStatusAsync(cancellationToken);
        }
        finally
        {
            _isRunning = false;
            _setupLock.Release();
        }
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        var installedModels = await ollamaApi.GetModelsAsync(cancellationToken);
        var installedNames = installedModels.Select(model => model.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var configured = GetConfiguredModels();

        lock (_status)
        {
            foreach (var model in configured)
            {
                var installed = installedNames.Contains(model.Name) || installedNames.Contains($"{model.Name}:latest");
                _status[model.Name] = new LocalModelStatus(
                    Name: model.Name,
                    Kind: model.Kind,
                    Required: model.PullOnStartup,
                    Installed: installed,
                    PullOnStartup: model.PullOnStartup,
                    Description: model.Description,
                    State: installed ? "installed" : "missing");
            }

            foreach (var installed in installedModels)
            {
                if (!_status.ContainsKey(installed.Name))
                {
                    _status[installed.Name] = new LocalModelStatus(installed.Name, "unknown", false, true, false, "Locally installed model.", "installed");
                }
            }
        }
    }

    private bool IsInstalled(string model)
    {
        lock (_status)
        {
            return _status.TryGetValue(model, out var status) && status.Installed;
        }
    }

    private IReadOnlyList<LocalModelSeed> GetConfiguredModels() => _options.RequiredModels.Count > 0
        ? _options.RequiredModels
        :
        [
            new LocalModelSeed(_options.DefaultModel, "chat", true, "Default chat model."),
            new LocalModelSeed(_options.Memory.EmbeddingModel, "embedding", _options.Memory.Enabled, "Default embedding model.")
        ];

    private async Task PullModelCoreAsync(string model, CancellationToken cancellationToken)
    {
        AddEvent($"Pulling local model {model}.");
        SetState(model, "pulling", null);

        await ollamaApi.PullModelAsync(model, cancellationToken);

        SetState(model, "installed", null);
        AddEvent($"Model {model} installed.");
    }

    private void SetState(string model, string state, string? error)
    {
        lock (_status)
        {
            if (_status.TryGetValue(model, out var status))
            {
                _status[model] = status with { State = state, Installed = state == "installed", Error = error };
                return;
            }

            _status[model] = new LocalModelStatus(model, "unknown", true, state == "installed", true, "Model requested at runtime.", state, error);
        }
    }

    private void AddEvent(string message)
    {
        var stamped = $"{DateTimeOffset.UtcNow:O} {message}";
        _events.Enqueue(stamped);
        logger.LogInformation("{Message}", message);

        while (_events.Count > 200 && _events.TryDequeue(out _))
        {
        }
    }
}
