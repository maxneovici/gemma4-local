using LLLMax.Api.Skills;

namespace LLLMax.Api.Endpoints;

public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/skills");

        group.MapGet("/", (ISkillRegistry skills) => Results.Ok(skills.GetSkills().Select(skill => new SkillListResponse(
            skill.Name,
            skill.Description,
            skill.Triggers,
            skill.Agents,
            skill.Priority,
            skill.Enabled,
            skill.FileName))));

        group.MapGet("/{name}", (string name, ISkillRegistry skills) =>
        {
            try
            {
                return Results.Ok(skills.GetRequiredSkill(name));
            }
            catch (InvalidOperationException exception)
            {
                return Results.NotFound(new { Error = exception.Message });
            }
        });

        group.MapPost("/relevant", (RelevantSkillRequest request, ISkillRegistry skills) =>
            Results.Ok(skills.FindRelevant(request.Agent, request.Message, request.Limit ?? 3)));

        return app;
    }
}
