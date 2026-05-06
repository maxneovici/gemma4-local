using LLLMax.Api.Integrations;

namespace LLLMax.Api.Endpoints;

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/integrations");

        group.MapGet("/apis", async (IApiIntegrationRegistry registry, CancellationToken cancellationToken) =>
            Results.Ok(await registry.ListAsync(cancellationToken)));

        group.MapPost("/apis/discover", async (ApiDiscoveryRequest request, IApiIntegrationRegistry registry, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await registry.DiscoverAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        group.MapPost("/apis/call", async (ApiCallRequest request, IApiIntegrationRegistry registry, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await registry.CallAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        return app;
    }
}
