using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Integrations;

public sealed class EfApiIntegrationRegistry(
    IDbContextFactory<LocalDbContext> dbFactory,
    LocalDataPaths paths,
    IHttpClientFactory httpClientFactory,
    IOptions<LocalAiOptions> options,
    ILogger<EfApiIntegrationRegistry> logger) : IApiIntegrationRegistry
{
    private const string ImportMarker = "api_integrations_json_imported";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalAiOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _imported;

    public async Task<IReadOnlyList<ApiIntegrationDefinition>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.ApiIntegrations
            .AsNoTracking()
            .OrderBy(integration => integration.Name)
            .Select(integration => ToModel(integration))
            .ToListAsync(cancellationToken);
    }

    public async Task<ApiIntegrationDefinition> DiscoverAsync(ApiDiscoveryRequest request, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);

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

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.ApiIntegrations.SingleOrDefaultAsync(integration => integration.Name.ToLower() == definition.Name.ToLower(), cancellationToken);

            if (existing is null)
            {
                db.ApiIntegrations.Add(ToEntity(definition));
            }
            else
            {
                Copy(definition, existing);
            }

            await db.SaveChangesAsync(cancellationToken);
            return definition;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ApiCallResponse> CallAsync(ApiCallRequest request, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);
        var integration = (await ListAsync(cancellationToken)).SingleOrDefault(item => string.Equals(item.Name, request.Name, StringComparison.OrdinalIgnoreCase))
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

    private async Task EnsureImportedAsync(CancellationToken cancellationToken) =>
        await JsonImport.ImportOnceAsync(dbFactory, _gate, () => _imported, () => _imported = true, ImportMarker, ImportJsonAsync, cancellationToken);

    private async Task ImportJsonAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.ApiRegistryPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(paths.ApiRegistryPath);
            var integrations = await JsonSerializer.DeserializeAsync<List<ApiIntegrationDefinition>>(stream, JsonOptions, cancellationToken) ?? [];

            foreach (var integration in integrations)
            {
                if (await db.ApiIntegrations.AnyAsync(existing => existing.Name.ToLower() == integration.Name.ToLower(), cancellationToken))
                {
                    continue;
                }

                db.ApiIntegrations.Add(ToEntity(integration));
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not import API registry file {File}", paths.ApiRegistryPath);
        }
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

    private static ApiIntegrationEntity ToEntity(ApiIntegrationDefinition definition)
    {
        var entity = new ApiIntegrationEntity { Id = Guid.NewGuid().ToString("n") };
        Copy(definition, entity);
        return entity;
    }

    private static void Copy(ApiIntegrationDefinition definition, ApiIntegrationEntity entity)
    {
        entity.Name = definition.Name;
        entity.BaseUrl = definition.BaseUrl;
        entity.OpenApiUrl = definition.OpenApiUrl;
        entity.DiscoveredAt = definition.DiscoveredAt;
        entity.OperationsJson = JsonSerializer.Serialize(definition.Operations, JsonOptions);
        entity.RawSchema = definition.RawSchema;
    }

    private static ApiIntegrationDefinition ToModel(ApiIntegrationEntity entity) =>
        new(
            Name: entity.Name,
            BaseUrl: entity.BaseUrl,
            OpenApiUrl: entity.OpenApiUrl,
            DiscoveredAt: entity.DiscoveredAt,
            Operations: JsonSerializer.Deserialize<IReadOnlyList<ApiOperationDefinition>>(entity.OperationsJson, JsonOptions) ?? [],
            RawSchema: entity.RawSchema);

    private static string Truncate(string text, int maxCharacters) =>
        text.Length <= maxCharacters ? text : text[..maxCharacters];
}
