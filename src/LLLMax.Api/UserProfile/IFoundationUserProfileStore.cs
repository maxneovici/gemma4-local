namespace LLLMax.Api.UserProfile;

public interface IFoundationUserProfileStore
{
    Task<FoundationUserProfile> GetAsync(CancellationToken cancellationToken);

    Task<FoundationUserProfile> SaveAsync(FoundationUserProfile profile, CancellationToken cancellationToken);
}
