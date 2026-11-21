namespace Abox.Kits;

/// <summary>
/// A service a kit authenticates to. The kits declare the services they need with the
/// <c>credential@1</c> capability; abox keeps the secrets of those services on the host.
/// </summary>
/// <param name="Service">The name of the service in the host credential store.</param>
/// <param name="Domains">The domains the API key is sent to; empty without an <c>apiKey</c> mechanism.</param>
/// <param name="Optional">Whether the sandbox starts without the credential.</param>
public sealed record KitService(
    string Service,
    IReadOnlyList<string> Domains,
    bool Optional = false
);
