namespace LLLMax.Api.Tools;

public interface ILocalToolRegistry
{
    IReadOnlyList<ILocalTool> GetTools();

    ILocalTool GetRequiredTool(string name);
}
