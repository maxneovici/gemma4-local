using LLLMax.Api.UserProfile;

namespace LLLMax.Api.Endpoints;

public static class UserProfileEndpoints
{
    public static IEndpointRouteBuilder MapUserProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/user-profile");

        group.MapGet("/", async (IFoundationUserProfileStore profiles, CancellationToken cancellationToken) =>
            Results.Ok(await profiles.GetAsync(cancellationToken)));

        group.MapPut("/", async (FoundationUserProfile profile, IFoundationUserProfileStore profiles, IInitialProfileMemorySeeder seeder, CancellationToken cancellationToken) =>
        {
            var existing = await profiles.GetAsync(cancellationToken);
            var saved = await profiles.SaveAsync(profile, cancellationToken);
            var seed = HasProfileData(saved) && !HasProfileData(existing)
                ? await seeder.SeedAsync(saved, cancellationToken)
                : null;

            return Results.Ok(new FoundationUserProfileSaveResponse(saved, seed));
        });

        return app;
    }

    private static bool HasProfileData(FoundationUserProfile profile) =>
        new[]
        {
            profile.Username,
            profile.Email,
            profile.FullName,
            profile.AssistantName,
            profile.AssistantDescription,
            profile.FamilyAndRelations,
            profile.Work,
            profile.Location,
            profile.CommunicationStyle,
            profile.Interests,
            profile.Goals,
            profile.Constraints,
            profile.Details,
            profile.Facts
        }.Any(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record FoundationUserProfileSaveResponse(FoundationUserProfile Profile, InitialProfileMemorySeedResponse? InitialSetup);
