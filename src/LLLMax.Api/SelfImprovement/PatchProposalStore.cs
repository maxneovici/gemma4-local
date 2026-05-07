using LLLMax.Api.Services;

namespace LLLMax.Api.SelfImprovement;

public sealed class PatchProposalStore(LocalDataPaths paths)
{
    public IReadOnlyList<PatchProposalSummary> List()
    {
        return Directory.EnumerateFiles(paths.PatchProposalsDirectory, "*.patch")
            .Select(ToSummary)
            .OrderByDescending(proposal => proposal.CreatedAt)
            .ToList();
    }

    public async Task<PatchProposalDetail?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var file = Resolve(id);

        if (file is null)
        {
            return null;
        }

        var info = new FileInfo(file);
        var content = await File.ReadAllTextAsync(file, cancellationToken);
        return new PatchProposalDetail(Path.GetFileNameWithoutExtension(file), info.Name, content, info.Length, info.CreationTimeUtc);
    }

    private string? Resolve(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains(Path.DirectorySeparatorChar) || id.Contains(Path.AltDirectorySeparatorChar))
        {
            return null;
        }

        var file = Path.Combine(paths.PatchProposalsDirectory, id.EndsWith(".patch", StringComparison.OrdinalIgnoreCase) ? id : $"{id}.patch");
        return File.Exists(file) ? file : null;
    }

    private static PatchProposalSummary ToSummary(string file)
    {
        var info = new FileInfo(file);
        return new PatchProposalSummary(Path.GetFileNameWithoutExtension(file), info.Name, info.Length, info.CreationTimeUtc);
    }
}
