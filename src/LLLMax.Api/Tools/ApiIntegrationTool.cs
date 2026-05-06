using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.Integrations;

namespace LLLMax.Api.Tools;

public sealed class ApiIntegrationTool(IApiIntegrationRegistry registry) : LocalToolBase<ApiIntegrationArguments>
{
    public override string Name => "api_integration";

    public override string Description => "Discover registered API schemas or call explicitly registered API endpoints through a unified local gateway.";

    protected override async Task<LocalToolResult> InvokeAsync(ApiIntegrationArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var action = arguments.Action.ToLowerInvariant();

        return action switch
        {
            "list" => new LocalToolResult(JsonSerializer.Serialize(await registry.ListAsync(cancellationToken))),
            "discover" => new LocalToolResult(JsonSerializer.Serialize(await registry.DiscoverAsync(new ApiDiscoveryRequest(
                Name: Require(arguments.Name, "name"),
                BaseUrl: Require(arguments.BaseUrl, "baseUrl"),
                OpenApiUrl: arguments.OpenApiUrl), cancellationToken))),
            "call" => new LocalToolResult(JsonSerializer.Serialize(await registry.CallAsync(new ApiCallRequest(
                Name: Require(arguments.Name, "name"),
                Method: arguments.Method ?? "GET",
                Path: Require(arguments.Path, "path"),
                Body: arguments.Body), cancellationToken))),
            _ => throw new ArgumentException("Unsupported api_integration action. Use list, discover, or call.")
        };
    }

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"Tool argument '{name}' is required for this action.") : value;
}

public sealed record ApiIntegrationArguments(
    [property: Required] string Action,
    string? Name = null,
    string? BaseUrl = null,
    string? OpenApiUrl = null,
    string? Method = null,
    string? Path = null,
    string? Body = null);
