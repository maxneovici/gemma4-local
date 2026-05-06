using LLLMax.Api.Agents;
using LLLMax.Api.Models;

namespace LLLMax.Api.Sessions;

public interface IToolUsePlanner
{
    ToolUseDecision Decide(ToolUsePlanningRequest request);
}

public sealed record ToolUsePlanningRequest(
    string Message,
    AgentDefinition Agent,
    bool ToolsAllowed,
    IReadOnlyList<LocalChatMessage> Messages);

public sealed record ToolUseDecision(
    string Policy,
    string Reason,
    IReadOnlyList<string> SuggestedTools)
{
    public bool ShouldRunTools => Policy.Equals(ToolUsePolicies.ToolRequired, StringComparison.OrdinalIgnoreCase);
}

public static class ToolUsePolicies
{
    public const string Direct = "direct";
    public const string ToolRequired = "tool_required";
    public const string BackgroundVerify = "background_verify";
    public const string Clarify = "clarify";
}

public sealed class ToolUsePlanner : IToolUsePlanner
{
    public ToolUseDecision Decide(ToolUsePlanningRequest request)
    {
        if (!request.ToolsAllowed)
        {
            return new ToolUseDecision(ToolUsePolicies.Direct, "Tools are disabled for this turn.", []);
        }

        var message = request.Message.Trim();
        var lowered = message.ToLowerInvariant();
        var suggested = SuggestedTools(lowered);

        if (suggested.Count > 0)
        {
            return new ToolUseDecision(ToolUsePolicies.ToolRequired, "The user explicitly requested work that depends on a local tool.", suggested);
        }

        if (IsAmbiguousToolReference(lowered))
        {
            return new ToolUseDecision(ToolUsePolicies.Clarify, "The user referred to a tool or external source, but the target is ambiguous.", []);
        }

        if (ShouldBackgroundVerify(lowered))
        {
            return new ToolUseDecision(ToolUsePolicies.BackgroundVerify, "The user asked a conceptual/system question that can be answered directly and optionally verified later.", []);
        }

        return new ToolUseDecision(ToolUsePolicies.Direct, "No tool dependency detected; answer directly.", []);
    }

    private static IReadOnlyList<string> SuggestedTools(string lowered)
    {
        var tools = new List<string>();

        AddIf(tools, "memory_search", lowered.Contains("search memory") || lowered.Contains("memory search") || lowered.Contains("look up in memory"));
        AddIf(tools, "memory_write", lowered.Contains("remember") || lowered.Contains("store memory") || lowered.Contains("save to memory"));
        AddIf(tools, "web_browse", lowered.Contains("browse") || lowered.Contains("web") || lowered.Contains("http://") || lowered.Contains("https://"));
        AddIf(tools, "api_integration", lowered.Contains("api") || lowered.Contains("call endpoint") || lowered.Contains("discover api"));
        AddIf(tools, "ocr_document", lowered.Contains("ocr"));
        AddIf(tools, "extract_invoice", lowered.Contains("invoice"));
        AddIf(tools, "document_vectorize_folder", lowered.Contains("document") || lowered.Contains("vectorize"));
        AddIf(tools, "delegate_to_agent", lowered.Contains("delegate") || lowered.Contains("subagent"));
        AddIf(tools, "safe_shell_command", lowered.Contains("run build") || lowered.Contains("run test") || lowered.Contains("shell"));
        AddIf(tools, "mcp:*", lowered.Contains("mcp"));

        return tools;
    }

    private static bool IsAmbiguousToolReference(string lowered) =>
        lowered.Contains("use a tool") || lowered.Contains("call a tool") || lowered.Contains("external source");

    private static bool ShouldBackgroundVerify(string lowered) =>
        lowered.Contains("is model routing") || lowered.Contains("does routing") || lowered.Contains("architecture") || lowered.Contains("why did");

    private static void AddIf(List<string> tools, string tool, bool condition)
    {
        if (condition)
        {
            tools.Add(tool);
        }
    }
}
