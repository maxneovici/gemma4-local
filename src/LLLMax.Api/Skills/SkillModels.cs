namespace LLLMax.Api.Skills;

public sealed record SkillDefinition(
    string Name,
    string Description,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> Agents,
    int Priority,
    bool Enabled,
    string Body,
    string FileName);

public sealed record SkillListResponse(
    string Name,
    string Description,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> Agents,
    int Priority,
    bool Enabled,
    string FileName);

public sealed record RelevantSkillRequest(string Agent, string Message, int? Limit = null);
