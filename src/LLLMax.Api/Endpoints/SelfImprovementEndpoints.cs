using LLLMax.Api.SelfImprovement;

namespace LLLMax.Api.Endpoints;

public static class SelfImprovementEndpoints
{
    public static IEndpointRouteBuilder MapSelfImprovementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/self-improvement");

        group.MapGet("/patch-proposals", (PatchProposalStore proposals) =>
            Results.Ok(proposals.List()));

        group.MapGet("/patch-proposals/{id}", async (string id, PatchProposalStore proposals, CancellationToken cancellationToken) =>
        {
            var proposal = await proposals.GetAsync(id, cancellationToken);
            return proposal is null ? Results.NotFound() : Results.Ok(proposal);
        });

        return app;
    }
}
