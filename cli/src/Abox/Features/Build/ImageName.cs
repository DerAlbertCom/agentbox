namespace Abox.Features.Build;

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
}
