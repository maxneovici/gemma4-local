namespace LLLMax.Api.Skills;

public interface ISkillRegistry
{
    IReadOnlyList<SkillDefinition> GetSkills();

    SkillDefinition GetRequiredSkill(string name);

    IReadOnlyList<SkillDefinition> FindRelevant(string agent, string message, int limit);
}
