using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Services;

public sealed class LocalDataPaths(IOptions<LocalAiOptions> options, IWebHostEnvironment environment)
{
    private readonly LocalAiOptions _options = options.Value;

    public string Root => Resolve(_options.DataDirectory);

    public string MemoryDirectory => Ensure(Path.Combine(Root, _options.Memory.StorageDirectory));

    public string DocumentDirectory => Ensure(Path.Combine(Root, _options.Documents.StorageDirectory));

    public string AgentsDirectory => Ensure(Path.Combine(Root, "agents"));

    public string SessionsDirectory => Ensure(Path.Combine(Root, "sessions"));

    public string SqliteDatabasePath => Path.Combine(EnsureRoot(), "lllmax.db");

    public string TaskGraphsDirectory => Ensure(Path.Combine(Root, "task-graphs"));

    public string ConsolidationJobsDirectory => Ensure(Path.Combine(Root, "consolidation-jobs"));

    public string BackgroundJobsDirectory => Ensure(Path.Combine(Root, "background-jobs"));

    public string BackgroundJobArtifactsDirectory => Ensure(Path.Combine(Root, "background-job-artifacts"));

    public string PatchProposalsDirectory => Ensure(Path.Combine(Root, "patch-proposals"));

    public string ApprovalsDirectory => Ensure(Path.Combine(Root, "approvals"));

    public string ApiRegistryPath => Path.Combine(Root, _options.ApiDiscovery.StorageFile);

    public string McpRegistryPath => Path.Combine(Root, _options.Mcp.StorageFile);

    public string EnsureRoot() => Ensure(Root);

    public string Resolve(string path) => Path.IsPathRooted(path)
        ? path
        : Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));

    public string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
