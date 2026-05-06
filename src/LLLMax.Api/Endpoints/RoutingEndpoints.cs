using LLLMax.Api.Sessions;

namespace LLLMax.Api.Endpoints;

public static class RoutingEndpoints
{
    public static IEndpointRouteBuilder MapRoutingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/routing");

        group.MapPost("/plan", async (RoutingPlanRequest request, IRoutingEvaluationService routing, CancellationToken cancellationToken) =>
            Results.Ok(await routing.PlanAsync(request.Message, request.Agent, request.AllowTools, cancellationToken)));

        group.MapPost("/evaluate", async (IRoutingEvaluationService routing, CancellationToken cancellationToken) =>
            Results.Ok(await routing.EvaluateAsync(cancellationToken)));

        return app;
    }
}

public sealed record RoutingPlanRequest(string Message, string? Agent = null, bool AllowTools = true);
