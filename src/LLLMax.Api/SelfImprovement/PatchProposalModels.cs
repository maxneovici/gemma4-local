namespace LLLMax.Api.SelfImprovement;

public sealed record PatchProposalSummary(
    string Id,
    string FileName,
    long Bytes,
    DateTimeOffset CreatedAt);

public sealed record PatchProposalDetail(
    string Id,
    string FileName,
    string Content,
    long Bytes,
    DateTimeOffset CreatedAt);
