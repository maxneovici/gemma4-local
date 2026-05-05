namespace Gemma4Local.Api.Services;

public sealed record LocalModelSetupSnapshot(
    bool IsRunning,
    bool IsComplete,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<LocalModelStatus> Models,
    IReadOnlyList<string> Events);

public sealed record LocalModelStatus(
    string Name,
    string Kind,
    bool Required,
    bool Installed,
    bool PullOnStartup,
    string Description,
    string State,
    string? Error = null);

public sealed record PullModelRequest(string Name);

public sealed record AvailableModelResponse(string Name, string Kind, bool Installed, bool PullOnStartup, string Description);
