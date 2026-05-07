using System.Text.Json;

namespace LLLMax.Api.BackgroundJobs;

public static class BackgroundJobKinds
{
    public const string DocumentVectorizeFolder = "document_vectorize_folder";

    public const string MemoryReport = "memory_report";
}

public static class BackgroundJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed record BackgroundJob(
    string Id,
    string Kind,
    string Status,
    string? Title,
    string? SessionId,
    string? Agent,
    JsonElement Payload,
    int ProgressCurrent,
    int ProgressTotal,
    string? StatusMessage,
    string? Result,
    string? Error,
    bool NotifySession,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt = null);

public sealed record BackgroundJobCreateRequest(
    string Kind,
    JsonElement Payload,
    string? Title = null,
    string? SessionId = null,
    string? Agent = null,
    bool NotifySession = true);

public sealed record BackgroundJobProgress(
    int Current,
    int Total,
    string Message,
    string? Result = null,
    string? Error = null);

public sealed record BackgroundJobListResponse(
    string Id,
    string Kind,
    string Status,
    string? Title,
    string? SessionId,
    int ProgressCurrent,
    int ProgressTotal,
    string? StatusMessage,
    string? Error,
    DateTimeOffset UpdatedAt);

public sealed record BackgroundJobArtifact(
    string Id,
    string JobId,
    string Kind,
    string Title,
    string ContentType,
    string FileName,
    long Bytes,
    DateTimeOffset CreatedAt);

public sealed record BackgroundJobArtifactCreateRequest(
    string Kind,
    string Title,
    string Content,
    string ContentType = "text/plain",
    string? FileName = null);
