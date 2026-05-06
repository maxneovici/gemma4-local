using System.Text.Json;
using System.Text.Json.Serialization;
using LLLMax.Api.Sessions;

namespace LLLMax.Api.Endpoints;

public static class SessionEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sessions");

        group.MapGet("/", async (IAssistantSessionStore sessions, CancellationToken cancellationToken) =>
            Results.Ok(await sessions.ListAsync(cancellationToken)));

        group.MapPost("/", async (SessionCreateRequest request, IAssistantSessionStore sessions, CancellationToken cancellationToken) =>
            Results.Ok(await sessions.CreateAsync(request, cancellationToken)));

        group.MapDelete("/", async (IAssistantSessionStore sessions, CancellationToken cancellationToken) =>
        {
            await sessions.DeleteAllAsync(cancellationToken);
            return Results.NoContent();
        });

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

        group.MapPost("/{id}/chat/stream", async (string id, SessionChatRequest request, IAssistantOrchestrator orchestrator, HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream";

            try
            {
                await foreach (var streamEvent in orchestrator.StreamChatAsync(id, request, cancellationToken))
                {
                    await context.Response.WriteAsync($"event: {streamEvent.Type}\n", cancellationToken);
                    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(streamEvent, SseJsonOptions)}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                var error = JsonSerializer.Serialize(new SessionChatStreamEvent("error", exception.Message), SseJsonOptions);
                await context.Response.WriteAsync("event: error\n", cancellationToken);
                await context.Response.WriteAsync($"data: {error}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        });

        return app;
    }
}
