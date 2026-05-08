using LLLMax.Api.UserProfile;

namespace LLLMax.Api.Endpoints;

public static class UserProfileEndpoints
{
    public static IEndpointRouteBuilder MapUserProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/user-profile");

        group.MapGet("/", async (IFoundationUserProfileStore profiles, CancellationToken cancellationToken) =>
            Results.Ok(await profiles.GetAsync(cancellationToken)));

        group.MapPut("/", async (FoundationUserProfile profile, IFoundationUserProfileStore profiles, CancellationToken cancellationToken) =>
            Results.Ok(await profiles.SaveAsync(profile, cancellationToken)));

        return app;
    }
}
