using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Services;

public interface IRuntimeModelSettings
{
    RuntimeModelSettingsSnapshot GetSnapshot();

    RuntimeModelSettingsSnapshot SetCoordinatorOverride(string? model);

    string GetCoordinatorModel();
}

public sealed class RuntimeModelSettings(IOptions<LocalAiOptions> options) : IRuntimeModelSettings
{
    private readonly LocalAiOptions _options = options.Value;
    private string? _coordinatorOverride;

    public RuntimeModelSettingsSnapshot GetSnapshot() => new(
        ConfiguredCoordinatorModel: ConfiguredCoordinatorModel,
        CoordinatorOverride: _coordinatorOverride,
        CurrentCoordinatorModel: GetCoordinatorModel());

    public RuntimeModelSettingsSnapshot SetCoordinatorOverride(string? model)
    {
        _coordinatorOverride = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        return GetSnapshot();
    }

    public string GetCoordinatorModel() => _coordinatorOverride ?? ConfiguredCoordinatorModel;

    private string ConfiguredCoordinatorModel => _options.Models.CoordinatorModel ?? _options.DefaultModel;
}

public sealed record RuntimeModelSettingsSnapshot(
    string ConfiguredCoordinatorModel,
    string? CoordinatorOverride,
    string CurrentCoordinatorModel);

public sealed record CoordinatorModelOverrideRequest(string? Model);
