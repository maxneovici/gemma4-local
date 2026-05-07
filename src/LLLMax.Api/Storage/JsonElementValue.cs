using System.Text.Json;

namespace LLLMax.Api.Storage;

public static class JsonElementValue
{
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static string Serialize(JsonElement element) => element.GetRawText();
}
