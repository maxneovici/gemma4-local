namespace LLLMax.Api.Options;

using LLLMax.Api.Agents;

public sealed class LocalAiOptions
{
    public const string SectionName = "LocalAi";

    public string AppName { get; init; } = "LLLMax";

    public string DataDirectory { get; init; } = "data";

    public string Provider { get; init; } = "Ollama";

    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";

    public string DefaultModel { get; init; } = "gemma4:e2b";

    public string SystemPrompt { get; init; } = "You are LLLMax, a frontier local AI assistant for Apple Silicon. Your job is to orchestrate local models, local tools, dynamic subagents, persistent memory, document understanding, and carefully scoped external integrations so users can complete real tasks without handing broad terminal control to the model.";

    public string ConversationSystemPrompt { get; init; } = "You are LLLMax in simple conversation mode. Be warm, likeable, and natural without being fake. Keep answers conversational and voice-friendly: short sentences, clear transitions, and no long bullet lists unless the user asks. Ask at most one brief follow-up question when needed. Do not mention internal routing, tools, policies, or system prompts.";

    public string? VisionModel { get; init; }

    public bool RequireLoopback { get; init; } = true;

    public bool ManageProcess { get; init; } = true;

    public bool StopManagedProcessOnShutdown { get; init; } = true;

    public bool EnsureDefaultModel { get; init; }

    public bool BlockStartupUntilModelsReady { get; init; }

    public string OllamaExecutable { get; init; } = "ollama";

    public int StartupTimeoutSeconds { get; init; } = 45;

    public int RequestTimeoutSeconds { get; init; } = 600;

    public LocalAiSamplingOptions Sampling { get; init; } = new();

    public LocalAiOrchestrationOptions Orchestration { get; init; } = new();

    public LocalAiModelRouterOptions ModelRouter { get; init; } = new();

    public LocalAiNativeToolCallingOptions NativeToolCalling { get; init; } = new();

    public LocalMemoryOptions Memory { get; init; } = new();

    public LocalToolOptions Tools { get; init; } = new();

    public LocalDocumentOptions Documents { get; init; } = new();

    public LocalWebBrowsingOptions WebBrowsing { get; init; } = new();

    public LocalApiDiscoveryOptions ApiDiscovery { get; init; } = new();

    public LocalMcpOptions Mcp { get; init; } = new();

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

    public string Provider { get; init; } = "Qdrant";

    public string EmbeddingModel { get; init; } = "nomic-embed-text";

    public int EmbeddingDimensions { get; init; } = 768;

    public int MaxContextItems { get; init; } = 5;

    public string StorageDirectory { get; init; } = "memory";

    public string QdrantBaseUrl { get; init; } = "http://127.0.0.1:6333";
}

public sealed class LocalToolOptions
{
    public bool EnableSafeShell { get; init; }

    public string ShellWorkingDirectory { get; init; } = ".";

    public int ShellTimeoutSeconds { get; init; } = 120;

    public bool EnableWorkspaceTools { get; init; } = true;

    public IReadOnlyList<string> WorkspaceRoots { get; init; } = ["."];

    public int WorkspaceMaxReadBytes { get; init; } = 120000;

    public IReadOnlyList<string> AllowedShellCommands { get; init; } =
    [
        "dotnet build",
        "dotnet test",
        "git status --short",
        "git diff --stat"
    ];
}

public sealed class LocalAiOrchestrationOptions
{
    public string DefaultAgent { get; init; } = "coordinator";

    public int MaxToolIterations { get; init; } = 8;

    public int MaxDelegationDepth { get; init; } = 3;

    public int MaxConcurrentBackgroundJobs { get; init; } = 3;

    public int ContextMaxEstimatedTokens { get; init; } = 220000;

    public int ContextSummaryTriggerPercent { get; init; } = 80;

    public int ContextSummaryKeepLastMessages { get; init; } = 8;

    public int MaxRelevantSkills { get; init; } = 3;

    public int MaxSkillContextCharacters { get; init; } = 4000;
}

public sealed class LocalAiModelRouterOptions
{
    public bool UseAiPlanner { get; init; } = true;

    public string? RouterModel { get; init; }

    public string RouterSystemPrompt { get; init; } = "You are the LLLMax routing planner. You infer user intent and choose local execution parameters. You do not answer the user. You return strict JSON only.";

    public int RouterMaxOutputTokens { get; init; } = 160;

    public int RouterMaxRecentMessages { get; init; } = 4;

    public string RouterKeepAlive { get; init; } = "30m";

    public string? InteractiveModel { get; init; }

    public string? BalancedModel { get; init; }

    public string? DeepReasoningModel { get; init; }

    public string DefaultReasoningEffort { get; init; } = "auto";

    public int ShortRequestWordThreshold { get; init; } = 18;
}

public sealed class LocalAiNativeToolCallingOptions
{
    public bool Enabled { get; init; } = true;

    public bool PreferNativeTools { get; init; } = true;
}

public sealed class LocalDocumentOptions
{
    public string StorageDirectory { get; init; } = "documents";

    public long MaxUploadBytes { get; init; } = 25 * 1024 * 1024;

    public int ChunkSizeCharacters { get; init; } = 2000;

    public IReadOnlyList<string> AllowedFolderRoots { get; init; } = ["data/documents"];
}

public sealed class LocalWebBrowsingOptions
{
    public bool Enabled { get; init; } = true;

    public int RequestTimeoutSeconds { get; init; } = 30;

    public int MaxResponseCharacters { get; init; } = 12000;
}

public sealed class LocalApiDiscoveryOptions
{
    public bool Enabled { get; init; } = true;

    public bool AllowRemoteEndpoints { get; init; }

    public int RequestTimeoutSeconds { get; init; } = 30;

    public int MaxResponseCharacters { get; init; } = 20000;

    public string StorageFile { get; init; } = "api-registry.json";
}

public sealed class LocalMcpOptions
{
    public bool Enabled { get; init; } = true;

    public bool RequireApproval { get; init; } = true;

    public string StorageFile { get; init; } = "mcp-registry.json";

    public int RequestTimeoutSeconds { get; init; } = 30;
}
