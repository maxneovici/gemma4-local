namespace LLLMax.Api.Integrations;

public interface IApiIntegrationRegistry
{
    Task<IReadOnlyList<ApiIntegrationDefinition>> ListAsync(CancellationToken cancellationToken);

    Task<ApiIntegrationDefinition> DiscoverAsync(ApiDiscoveryRequest request, CancellationToken cancellationToken);

    Task<ApiCallResponse> CallAsync(ApiCallRequest request, CancellationToken cancellationToken);
}
