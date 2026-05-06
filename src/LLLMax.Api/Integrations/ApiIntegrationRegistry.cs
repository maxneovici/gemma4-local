using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Integrations;

public sealed class ApiIntegrationRegistry(LocalDataPaths paths, IHttpClientFactory httpClientFactory, IOptions<LocalAiOptions> options) : IApiIntegrationRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalAiOptions _options = options.Value;

    public async Task<IReadOnlyList<ApiIntegrationDefinition>> ListAsync(CancellationToken cancellationToken) =>
        await ReadAllAsync(cancellationToken);

    public async Task<ApiIntegrationDefinition> DiscoverAsync(ApiDiscoveryRequest request, CancellationToken cancellationToken)
    {
        if (!_options.ApiDiscovery.Enabled)
        {
            throw new InvalidOperationException("API discovery is disabled.");
        }

        EnsureEndpointAllowed(request.BaseUrl);
        var rawSchema = await TryFetchSchemaAsync(request, cancellationToken);
        var definition = new ApiIntegrationDefinition(
            Name: request.Name,
            BaseUrl: request.BaseUrl.TrimEnd('/'),
            OpenApiUrl: request.OpenApiUrl,
            DiscoveredAt: DateTimeOffset.UtcNow,
            Operations: ParseOperations(rawSchema),
            RawSchema: rawSchema);

        var integrations = (await ReadAllAsync(cancellationToken))
            .Where(integration => !string.Equals(integration.Name, definition.Name, StringComparison.OrdinalIgnoreCase))
            .Concat([definition])
            .OrderBy(integration => integration.Name)
            .ToList();

        await WriteAllAsync(integrations, cancellationToken);

        return definition;
    }

    public async Task<ApiCallResponse> CallAsync(ApiCallRequest request, CancellationToken cancellationToken)
    {
        var integration = (await ReadAllAsync(cancellationToken)).SingleOrDefault(item => string.Equals(item.Name, request.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"API integration '{request.Name}' is not registered.");

        EnsureEndpointAllowed(integration.BaseUrl);

        var uri = new Uri(new Uri($"{integration.BaseUrl}/"), request.Path.TrimStart('/'));
        using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), uri);

        foreach (var header in request.Headers ?? new Dictionary<string, string>())
        {
            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Body is not null)
        {
            httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json"));
        }

        using var response = await httpClientFactory.CreateClient("integrations").SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ApiCallResponse(
            StatusCode: (int)response.StatusCode,
            ContentType: response.Content.Headers.ContentType?.ToString() ?? "text/plain",
            Body: Truncate(body, _options.ApiDiscovery.MaxResponseCharacters));
    }

    private async Task<string?> TryFetchSchemaAsync(ApiDiscoveryRequest request, CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            request.OpenApiUrl,
            new Uri(new Uri(request.BaseUrl), "/openapi.json").ToString(),
            new Uri(new Uri(request.BaseUrl), "/swagger/v1/swagger.json").ToString()
        }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var url in candidates)
        {
            EnsureEndpointAllowed(url);

            try
            {
                return Truncate(await httpClientFactory.CreateClient("integrations").GetStringAsync(url, cancellationToken), _options.ApiDiscovery.MaxResponseCharacters);
            }
            catch (HttpRequestException)
            {
            }
        }

        return null;
    }

    private static IReadOnlyList<ApiOperationDefinition> ParseOperations(string? rawSchema)
    {
        if (string.IsNullOrWhiteSpace(rawSchema))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawSchema);

            if (!document.RootElement.TryGetProperty("paths", out var pathsElement))
            {
                return [];
            }

            return pathsElement.EnumerateObject()
                .SelectMany(path => path.Value.EnumerateObject().Select(method => new ApiOperationDefinition(
                    Method: method.Name.ToUpperInvariant(),
                    Path: path.Name,
                    OperationId: method.Value.TryGetProperty("operationId", out var operationId) ? operationId.GetString() : null,
                    Summary: method.Value.TryGetProperty("summary", out var summary) ? summary.GetString() : null)))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<List<ApiIntegrationDefinition>> ReadAllAsync(CancellationToken cancellationToken)
    {
        paths.EnsureRoot();
        var file = paths.ApiRegistryPath;

        if (!File.Exists(file))
        {
            return [];
        }

        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<List<ApiIntegrationDefinition>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteAllAsync(IReadOnlyList<ApiIntegrationDefinition> integrations, CancellationToken cancellationToken)
    {
        paths.EnsureRoot();
        await using var stream = File.Create(paths.ApiRegistryPath);
        await JsonSerializer.SerializeAsync(stream, integrations, JsonOptions, cancellationToken);
    }

    private void EnsureEndpointAllowed(string url)
    {
        if (_options.ApiDiscovery.AllowRemoteEndpoints)
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !LocalEndpointGuard.IsLoopback(uri.ToString()))
        {
            throw new InvalidOperationException("API discovery/calls are restricted to loopback unless LocalAi:ApiDiscovery:AllowRemoteEndpoints=true.");
        }
    }

    private static string Truncate(string text, int maxCharacters) =>
        text.Length <= maxCharacters ? text : text[..maxCharacters];
}
