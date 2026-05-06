using LLLMax.Api.Mcp;

namespace LLLMax.Api.Endpoints;

public static class McpEndpoints
{
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/mcp");

        group.MapGet("/servers", async (IMcpRegistry registry, CancellationToken cancellationToken) =>
            Results.Ok(await registry.ListAsync(cancellationToken)));

        group.MapPost("/servers", async (McpRegisterServerRequest request, IMcpRegistry registry, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await registry.RegisterAsync(request, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { Error = exception.Message });
            }
        });

        group.MapPost("/servers/{serverId}/tools/{toolName}/approve", async (string serverId, string toolName, IMcpRegistry registry, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await registry.ApproveToolAsync(serverId, toolName, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        group.MapPost("/servers/{serverId}/tools/{toolName}/reject", async (string serverId, string toolName, IMcpRegistry registry, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await registry.RejectToolAsync(serverId, toolName, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        return app;
    }
}
