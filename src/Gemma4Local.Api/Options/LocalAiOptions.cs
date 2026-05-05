namespace Gemma4Local.Api.Options;

using Gemma4Local.Api.Agents;

public sealed class LocalAiOptions
{
    public const string SectionName = "LocalAi";

    public string Provider { get; init; } = "Ollama";

    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";

    public string DefaultModel { get; init; } = "gemma4:e2b";

    public string SystemPrompt { get; init; } = "You are a concise, practical local assistant.";

    public bool RequireLoopback { get; init; } = true;

    public bool ManageProcess { get; init; } = true;

    public bool StopManagedProcessOnShutdown { get; init; } = true;

    public bool EnsureDefaultModel { get; init; }

    public bool BlockStartupUntilModelsReady { get; init; }

    public string OllamaExecutable { get; init; } = "ollama";

    public int StartupTimeoutSeconds { get; init; } = 45;

    public int RequestTimeoutSeconds { get; init; } = 600;

    public LocalAiSamplingOptions Sampling { get; init; } = new();

    public LocalMemoryOptions Memory { get; init; } = new();

    public LocalToolOptions Tools { get; init; } = new();

    public IReadOnlyList<AgentDefinition> Agents { get; init; } = [];

    public IReadOnlyList<LocalModelSeed> RequiredModels { get; init; } = [];
}

public sealed record LocalModelSeed(string Name, string Kind, bool PullOnStartup, string Description);

public sealed class LocalAiSamplingOptions
{
    public double Temperature { get; init; } = 1.0;

    public double TopP { get; init; } = 0.95;

    public int TopK { get; init; } = 64;
}

public sealed class LocalMemoryOptions
{
    public bool Enabled { get; init; } = true;

    public string Provider { get; init; } = "InMemory";

    public string EmbeddingModel { get; init; } = "nomic-embed-text";

    public int EmbeddingDimensions { get; init; } = 768;

    public int MaxContextItems { get; init; } = 5;

    public string QdrantBaseUrl { get; init; } = "http://127.0.0.1:6333";
}

public sealed class LocalToolOptions
{
    public bool EnableSafeShell { get; init; }

    public string ShellWorkingDirectory { get; init; } = ".";

    public int ShellTimeoutSeconds { get; init; } = 120;

    public IReadOnlyList<string> AllowedShellCommands { get; init; } =
    [
        "dotnet build",
        "dotnet test",
        "git status --short",
        "git diff --stat"
    ];
}
