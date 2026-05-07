using System.Text.Json;
using LLLMax.Api.Agents;
using LLLMax.Api.Tools;

namespace LLLMax.Api.Endpoints;

public static class SmartHomeEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSmartHomeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/smart-home");

        group.MapPost("/invoke", async (
            SmartHomeArguments request,
            ILocalToolRegistry tools,
            IAgentRegistry agents,
            CancellationToken cancellationToken) =>
        {
            var tool = tools.GetRequiredTool("smart_home");
            var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(request, JsonOptions), JsonOptions) ?? [];
            var result = await tool.InvokeAsync(new LocalToolInvocation(
                ToolName: tool.Name,
                Arguments: arguments,
                Agent: agents.GetRequiredAgent("coordinator"),
                ConversationId: null,
                DelegationDepth: 0), cancellationToken);

            return Results.Ok(new { result.Content });
        });

        return app;
    }
}
