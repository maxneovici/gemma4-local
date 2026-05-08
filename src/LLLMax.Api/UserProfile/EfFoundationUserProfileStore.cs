using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.UserProfile;

public sealed class EfFoundationUserProfileStore(IDbContextFactory<LocalDbContext> dbFactory) : IFoundationUserProfileStore
{
    private const string ProfileId = "foundation";

    public async Task<FoundationUserProfile> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(profile => profile.Id == ProfileId, cancellationToken);
        return entity is null ? new FoundationUserProfile() : ToModel(entity);
    }

    public async Task<FoundationUserProfile> SaveAsync(FoundationUserProfile profile, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UserProfiles.SingleOrDefaultAsync(existing => existing.Id == ProfileId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (entity is null)
        {
            entity = new UserProfileEntity
            {
                Id = ProfileId,
                CreatedAt = now
            };
            db.UserProfiles.Add(entity);
        }

        entity.Username = profile.Username.Trim();
        entity.Email = profile.Email.Trim();
        entity.FullName = profile.FullName.Trim();
        entity.FamilyAndRelations = profile.FamilyAndRelations.Trim();
        entity.Work = profile.Work.Trim();
        entity.Location = profile.Location.Trim();
        entity.CommunicationStyle = profile.CommunicationStyle.Trim();
        entity.Interests = profile.Interests.Trim();
        entity.Goals = profile.Goals.Trim();
        entity.Constraints = profile.Constraints.Trim();
        entity.Details = profile.Details.Trim();
        entity.Facts = profile.Facts.Trim();
        entity.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    private static FoundationUserProfile ToModel(UserProfileEntity entity) =>
        new(
            entity.Username,
            entity.Email,
            entity.FullName,
            entity.FamilyAndRelations,
            entity.Work,
            entity.Location,
            entity.CommunicationStyle,
            entity.Interests,
            entity.Goals,
            entity.Constraints,
            entity.Details,
            entity.Facts,
            entity.UpdatedAt);
}
