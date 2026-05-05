namespace Gemma4Local.Api.Tools;

public sealed class LocalToolRegistry(IEnumerable<ILocalTool> tools) : ILocalToolRegistry
{
    private readonly IReadOnlyList<ILocalTool> _tools = tools.ToList();

    public IReadOnlyList<ILocalTool> GetTools() => _tools;

    public ILocalTool GetRequiredTool(string name) =>
        _tools.SingleOrDefault(tool => string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Tool '{name}' is not registered.");
}
