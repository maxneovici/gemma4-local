using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.Storage;

[Index(nameof(UpdatedAt))]
public sealed class SessionEntity
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Agent { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string? Summary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<SessionMessageEntity> Messages { get; set; } = [];
}

public sealed class SessionMessageEntity
{
    public string SessionId { get; set; } = string.Empty;

    public int Ordinal { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? TraceId { get; set; }

    public string? ReasoningStepsJson { get; set; }

    public string? TaskGraphJson { get; set; }

    public string? ToolTracesJson { get; set; }

    public string? CitationsJson { get; set; }

    public SessionEntity? Session { get; set; }
}

public sealed class AppMetadataEntity
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

[Index(nameof(UpdatedAt))]
public sealed class UserProfileEntity
{
    public string Id { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string FamilyAndRelations { get; set; } = string.Empty;

    public string Work { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string CommunicationStyle { get; set; } = string.Empty;

    public string Interests { get; set; } = string.Empty;

    public string Goals { get; set; } = string.Empty;

    public string Constraints { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public string Facts { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(UpdatedAt))]
public sealed class BackgroundJobEntity
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? SessionId { get; set; }

    public string? Agent { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public int ProgressCurrent { get; set; }

    public int ProgressTotal { get; set; }

    public string? StatusMessage { get; set; }

    public string? Result { get; set; }

    public string? Error { get; set; }

    public bool NotifySession { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

[Index(nameof(JobId))]
[Index(nameof(CreatedAt))]
public sealed class BackgroundJobArtifactEntity
{
    public string Id { get; set; } = string.Empty;

    public string JobId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

[Index(nameof(CreatedAt))]
public sealed class DocumentEntity
{
    public string Id { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public string? ContentType { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

[Index(nameof(UpdatedAt))]
public sealed class ApprovalEntity
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? ConversationId { get; set; }

    public string? DecisionReason { get; set; }

    public string? Scope { get; set; }
}

[Index(nameof(UpdatedAt))]
public sealed class TaskGraphEntity
{
    public string Id { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? ActiveNodeId { get; set; }

    public double Confidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string GraphJson { get; set; } = "{}";
}

[Index(nameof(UpdatedAt))]
public sealed class MemoryConsolidationJobEntity
{
    public string Id { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int MemoriesWritten { get; set; }

    public string? Summary { get; set; }

    public string? Error { get; set; }

    public string? TaskGraphId { get; set; }
}

[Index(nameof(Name), IsUnique = true)]
public sealed class ApiIntegrationEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string? OpenApiUrl { get; set; }

    public DateTimeOffset DiscoveredAt { get; set; }

    public string OperationsJson { get; set; } = "[]";

    public string? RawSchema { get; set; }
}

[Index(nameof(Name), IsUnique = true)]
public sealed class McpServerEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Transport { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<McpToolEntity> Tools { get; set; } = [];
}

public sealed class McpToolEntity
{
    public string ServerId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ArgumentsJsonSchema { get; set; } = "{}";

    public string ApprovalStatus { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public McpServerEntity? Server { get; set; }
}
