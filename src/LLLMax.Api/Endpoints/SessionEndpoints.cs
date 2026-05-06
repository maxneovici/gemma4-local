using LLLMax.Api.Sessions;

namespace LLLMax.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sessions");

        group.MapGet("/", async (IAssistantSessionStore sessions, CancellationToken cancellationToken) =>
            Results.Ok(await sessions.ListAsync(cancellationToken)));

        group.MapPost("/", async (SessionCreateRequest request, IAssistantSessionStore sessions, CancellationToken cancellationToken) =>
            Results.Ok(await sessions.CreateAsync(request, cancellationToken)));

        group.MapGet("/{id}", async (string id, IAssistantSessionStore sessions, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await sessions.GetAsync(id, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        group.MapPost("/{id}/chat", async (string id, SessionChatRequest request, IAssistantOrchestrator orchestrator, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await orchestrator.ChatAsync(id, request, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { Error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        return app;
    }
}
