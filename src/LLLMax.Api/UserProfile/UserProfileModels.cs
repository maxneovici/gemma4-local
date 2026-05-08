namespace LLLMax.Api.UserProfile;

public sealed record FoundationUserProfile(
    string Username = "",
    string Email = "",
    string FullName = "",
    string Details = "",
    string Facts = "",
    DateTimeOffset? UpdatedAt = null);
