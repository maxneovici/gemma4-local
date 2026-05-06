using LLLMax.Api.Models;

namespace LLLMax.Api.Tools;

public interface ILocalTool
{
    string Name { get; }

    string Description { get; }

    string ArgumentsJsonSchema { get; }

    OllamaToolDefinition ToOllamaToolDefinition();

    Task<LocalToolResult> InvokeAsync(LocalToolInvocation invocation, CancellationToken cancellationToken);
}
