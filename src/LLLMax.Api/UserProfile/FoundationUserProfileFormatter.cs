using System.Text;

namespace LLLMax.Api.UserProfile;

public static class FoundationUserProfileFormatter
{
    public static string FormatForPrompt(FoundationUserProfile profile)
    {
        var lines = new List<string>();

        Add(lines, "username", profile.Username);
        Add(lines, "email", profile.Email);
        Add(lines, "full name", profile.FullName);
        Add(lines, "family and relations", profile.FamilyAndRelations);
        Add(lines, "work", profile.Work);
        Add(lines, "location", profile.Location);
        Add(lines, "communication style", profile.CommunicationStyle);
        Add(lines, "interests", profile.Interests);
        Add(lines, "goals", profile.Goals);
        Add(lines, "constraints", profile.Constraints);
        Add(lines, "details", profile.Details);
        Add(lines, "facts", profile.Facts);

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Foundation user profile. This is explicit user-editable profile data and should take precedence over learned memory when there is a conflict.");

        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static void Add(ICollection<string> lines, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"- {label}: {value.Trim()}");
        }
    }
}
