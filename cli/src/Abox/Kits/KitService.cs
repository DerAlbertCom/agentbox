namespace Abox.Kits;

/// <summary>
/// A service a kit authenticates to. The kits declare the services they need with the
/// <c>credential@1</c> capability; abox keeps the secrets of those services on the host.
/// </summary>
/// <param name="Service">The name of the service in the host credential store.</param>
/// <param name="Phases">The phases the credential is used in.</param>
/// <param name="EnvironmentVariable">The variable the agents read, when the kit names one.</param>
/// <param name="Domains">The hosts the credential is sent to.</param>
public sealed record KitService(
    string Service,
    IReadOnlyList<string> Phases,
    string? EnvironmentVariable,
    IReadOnlyList<string> Domains
);
