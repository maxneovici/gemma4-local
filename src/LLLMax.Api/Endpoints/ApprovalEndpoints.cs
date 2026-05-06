using LLLMax.Api.Approvals;

namespace LLLMax.Api.Endpoints;

public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/approvals");

        group.MapGet("/", async (IApprovalService approvals, CancellationToken cancellationToken) =>
            Results.Ok(await approvals.ListAsync(cancellationToken)));

        group.MapGet("/{id}", async (string id, IApprovalService approvals, CancellationToken cancellationToken) =>
        {
            var approval = await approvals.GetAsync(id, cancellationToken);
            return approval is null ? Results.NotFound() : Results.Ok(approval);
        });

        group.MapPost("/{id}/approve", async (string id, ApprovalDecisionRequest request, IApprovalService approvals, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await approvals.ApproveAsync(id, request.Reason, request.Scope, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        group.MapPost("/{id}/reject", async (string id, ApprovalDecisionRequest request, IApprovalService approvals, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await approvals.RejectAsync(id, request.Reason, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        return app;
    }
}
