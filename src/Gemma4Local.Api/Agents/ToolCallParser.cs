using System.Text.Json;

namespace Gemma4Local.Api.Agents;

public static class ToolCallParser
{
    public static bool TryParse(string text, out ParsedToolCall toolCall)
    {
        toolCall = default!;

        var trimmed = text.Trim();

        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (!root.TryGetProperty("tool", out var toolElement) || toolElement.GetString() is not { Length: > 0 } tool)
            {
                return false;
            }

            var arguments = root.TryGetProperty("arguments", out var argumentsElement)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsElement.GetRawText()) ?? []
                : [];

            toolCall = new ParsedToolCall(tool, arguments);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record ParsedToolCall(string Tool, IReadOnlyDictionary<string, JsonElement> Arguments);
