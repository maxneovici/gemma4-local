namespace LLLMax.Api.UserProfile;

public sealed record FoundationUserProfile(
    string Username = "",
    string Email = "",
    string FullName = "",
    string FamilyAndRelations = "",
    string Work = "",
    string Location = "",
    string CommunicationStyle = "",
    string Interests = "",
    string Goals = "",
    string Constraints = "",
    string Details = "",
    string Facts = "",
    DateTimeOffset? UpdatedAt = null);
