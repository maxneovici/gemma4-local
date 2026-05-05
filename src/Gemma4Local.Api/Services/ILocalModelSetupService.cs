namespace Gemma4Local.Api.Services;

public interface ILocalModelSetupService
{
    LocalModelSetupSnapshot GetSnapshot();

    Task EnsureStartupModelsAsync(CancellationToken cancellationToken);

    Task PullModelAsync(string model, CancellationToken cancellationToken);
}
