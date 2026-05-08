namespace LLLMax.Api.Memory;

public static class MemoryLayers
{
    public const string Memory = "memory";
    public const string Knowledge = "knowledge";

    public const string LayerKey = "layer";

    public static Dictionary<string, string> WithLayer(IReadOnlyDictionary<string, string>? metadata, string layer)
    {
        var result = new Dictionary<string, string>(metadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [LayerKey] = layer
        };

        return result;
    }
}
