using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Services;

public sealed class LocalModelSetupHostedService(
    ILocalModelSetupService setupService,
    IOptions<LocalAiOptions> options,
    ILogger<LocalModelSetupHostedService> logger) : IHostedService
{
    private readonly LocalAiOptions _options = options.Value;
    private Task? _setupTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var hasStartupModels = _options.RequiredModels.Count > 0
            ? _options.RequiredModels.Any(model => model.PullOnStartup)
            : _options.Memory.Enabled || !string.IsNullOrWhiteSpace(_options.DefaultModel);

        if (!hasStartupModels && !_options.EnsureDefaultModel)
        {
            return Task.CompletedTask;
        }

        if (_options.BlockStartupUntilModelsReady)
        {
            return setupService.EnsureStartupModelsAsync(cancellationToken);
        }

        _setupTask = Task.Run(async () =>
        {
            try
            {
                await setupService.EnsureStartupModelsAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Local model setup failed.");
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_setupTask is not null)
        {
            await _setupTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
