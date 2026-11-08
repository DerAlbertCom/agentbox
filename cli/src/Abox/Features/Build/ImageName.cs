namespace Abox.Features.Build;

/// <summary>
/// The name a team's kit set is published under. The registry host is not wired yet,
/// so this is only the part abox owns: a tenant prefix and the team.
/// </summary>
public static class ImageName
{
    /// <summary>The tenant of the installs that do not name one.</summary>
    public const string Tenant = "rwest";

    /// <summary>The reference of the kit set of a team, registry host not included.</summary>
    public static string OfTeam(string team, string tag) => $"agentbox-{Tenant}-{team}:{tag}";
}
