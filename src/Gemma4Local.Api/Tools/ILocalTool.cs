namespace Gemma4Local.Api.Tools;

public interface ILocalTool
{
    string Name { get; }

    string Description { get; }

    string ArgumentsJsonSchema { get; }

    Task<LocalToolResult> InvokeAsync(LocalToolInvocation invocation, CancellationToken cancellationToken);
}
