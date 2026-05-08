using System.Text.Json;

namespace LLLMax.Api.Agents;

public static class ToolCallParser
{
    public static bool TryParse(string text, out ParsedToolCall toolCall)
    {
        toolCall = default!;

        var trimmed = ExtractJsonObject(text.Trim());

        if (trimmed is null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (!TryGetToolName(root, out var tool))
            {
                return false;
            }

            var arguments = root.TryGetProperty("arguments", out var argumentsElement)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsElement.GetRawText()) ?? []
                : root.EnumerateObject()
                    .Where(property => !property.NameEquals("tool") && !property.NameEquals("tool_name"))
                    .ToDictionary(property => property.Name, property => property.Value.Clone());

            toolCall = new ParsedToolCall(tool, arguments);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetToolName(JsonElement root, out string tool)
    {
        if (root.TryGetProperty("tool", out var toolElement) && toolElement.GetString() is { Length: > 0 } parsedTool)
        {
            tool = parsedTool;
            return true;
        }

        if (root.TryGetProperty("tool_name", out var toolNameElement) && toolNameElement.GetString() is { Length: > 0 } parsedToolName)
        {
            tool = parsedToolName;
            return true;
        }

        tool = string.Empty;
        return false;
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');

        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (character == '{')
            {
                depth++;
            }

            if (character == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return text[start..(index + 1)];
                }
            }
        }

        return null;
    }
}

public sealed record ParsedToolCall(string Tool, IReadOnlyDictionary<string, JsonElement> Arguments);
