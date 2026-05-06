namespace LLLMax.Api.Integrations;

public sealed record ApiDiscoveryRequest(string Name, string BaseUrl, string? OpenApiUrl = null);

public sealed record ApiIntegrationDefinition(
    string Name,
    string BaseUrl,
    string? OpenApiUrl,
    DateTimeOffset DiscoveredAt,
    IReadOnlyList<ApiOperationDefinition> Operations,
    string? RawSchema);

public sealed record ApiOperationDefinition(string Method, string Path, string? OperationId, string? Summary);

public sealed record ApiCallRequest(
    string Name,
    string Method,
    string Path,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Body = null);

public sealed record ApiCallResponse(int StatusCode, string ContentType, string Body);
