namespace Abox.Kits;

/// <summary>
/// A service a kit authenticates to. The kits declare the services they need with the
/// <c>credential@1</c> capability; abox keeps the secrets of those services on the host.
/// </summary>
/// <param name="Service">The name of the service in the host credential store.</param>
public sealed record KitService(string Service);
