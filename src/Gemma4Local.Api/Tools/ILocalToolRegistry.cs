namespace Gemma4Local.Api.Tools;

public interface ILocalToolRegistry
{
    IReadOnlyList<ILocalTool> GetTools();

    ILocalTool GetRequiredTool(string name);
}
