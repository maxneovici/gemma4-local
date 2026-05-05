using Gemma4Local.Api.Agents;
using Gemma4Local.Api.Tools;

namespace Gemma4Local.Api.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agents");

        group.MapGet("/", (IAgentRegistry agentRegistry) => Results.Ok(agentRegistry.GetAgents().Select(agent => new AgentListResponse(
            Name: agent.Name,
            Description: agent.Description,
            Model: agent.Model,
            AllowedTools: agent.AllowedTools ?? [],
            AllowedAgents: agent.AllowedAgents ?? []))));

        group.MapGet("/tools", (ILocalToolRegistry toolRegistry) => Results.Ok(toolRegistry.GetTools().Select(tool => new
        {
            tool.Name,
            tool.Description,
            tool.ArgumentsJsonSchema
        })));

        group.MapPost("/run", async (AgentRunRequest request, IAgentRuntime agentRuntime, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await agentRuntime.RunAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { Error = exception.Message });
            }
        });

        return app;
    }
}
