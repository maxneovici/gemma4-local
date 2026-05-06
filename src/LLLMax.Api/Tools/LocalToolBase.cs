using System.Text.Json;
using System.Text.Json.Nodes;
using LLLMax.Api.Models;
using NJsonSchema;

namespace LLLMax.Api.Tools;

public abstract class LocalToolBase<TArguments> : ILocalTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string SchemaJson = ToolJsonSchema.From<TArguments>();
    private static readonly JsonSchema Schema = JsonSchema.FromJsonAsync(SchemaJson).GetAwaiter().GetResult();

    public abstract string Name { get; }

    public abstract string Description { get; }

    public string ArgumentsJsonSchema => SchemaJson;

    public OllamaToolDefinition ToOllamaToolDefinition() => new(
        Type: "function",
        Function: new OllamaToolFunction(
            Name: Name,
            Description: Description,
            Parameters: JsonNode.Parse(ArgumentsJsonSchema) ?? new JsonObject()));

    public async Task<LocalToolResult> InvokeAsync(LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = Deserialize(invocation.Arguments);
        return await InvokeAsync(arguments, invocation, cancellationToken);
    }

    protected abstract Task<LocalToolResult> InvokeAsync(TArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken);

    private static TArguments Deserialize(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var json = JsonSerializer.Serialize(arguments, JsonOptions);
        var errors = Schema.Validate(json);

        if (errors.Count > 0)
        {
            throw new ArgumentException($"Tool arguments failed schema validation: {string.Join("; ", errors.Select(error => error.ToString()))}");
        }

        var result = JsonSerializer.Deserialize<TArguments>(json, JsonOptions)
            ?? throw new ArgumentException("Tool arguments could not be deserialized.");

        return result;
    }
}
