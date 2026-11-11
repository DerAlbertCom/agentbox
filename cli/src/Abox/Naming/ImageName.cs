using System.Diagnostics.CodeAnalysis;

namespace Abox.Naming;

/// <summary>
/// The unqualified team reference kept in local build manifests. Publishing adds
/// a registry prefix and a stable, separate squad path segment.
/// </summary>
public static class ImageName
{
    /// <summary>The tenant of the installs that do not name one.</summary>
    public const string Tenant = "rwest";

    /// <summary>The reference of the kit set of a team, registry host not included.</summary>
    public static string OfTeam(string team, string tag) => $"agentbox-{Tenant}-{team}:{tag}";

    /// <summary>Reads the team from an unqualified local build reference with the expected tag.</summary>
    public static bool TryParseTeam(
        string? reference,
        string tag,
        [NotNullWhen(true)] out string? team
    )
    {
        var prefix = $"agentbox-{Tenant}-";
        var suffix = ":" + tag;
        if (
            reference is not null
            && reference.StartsWith(prefix, StringComparison.Ordinal)
            && reference.EndsWith(suffix, StringComparison.Ordinal)
            && reference.Length > prefix.Length + suffix.Length
        )
        {
            team = reference[prefix.Length..^suffix.Length];
            return true;
        }
        team = null;
        return false;
    }
}
