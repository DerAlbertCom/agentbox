namespace Abox.Sbx;

/// <summary>
/// The secrets the host keeps.
/// </summary>
/// <param name="Services">The names of the services sbx knows.</param>
/// <param name="Custom">The custom secrets: the hosts they are sent to and the variable the agents read.</param>
public sealed record HostSecrets(
    IReadOnlyList<string> Services,
    IReadOnlyList<CustomSecret> Custom
);

/// <summary>
/// A secret of a service sbx does not know.
/// </summary>
/// <param name="Targets">The hosts the secret is sent to.</param>
/// <param name="EnvironmentVariable">The variable the agents read.</param>
public sealed record CustomSecret(IReadOnlyList<string> Targets, string? EnvironmentVariable);
