namespace Abox.Kits;

/// <summary>
/// How a kit is written down: a local directory under the kits folder, or a registry
/// reference such as <c>docker.io/acme/pi-config:1.0</c>.
/// </summary>
public static class KitReference
{
    /// <summary>
    /// The repository of a reference: the name without its tag. A local kit has no tag,
    /// so its name is its repository.
    /// </summary>
    public static string RepositoryOf(string reference)
    {
        var lastSlash = reference.LastIndexOf('/');
        var lastColon = reference.LastIndexOf(':');

        // A colon after the last slash is a tag; a colon before it is a registry port.
        return lastColon > lastSlash ? reference[..lastColon] : reference;
    }

    /// <summary>Whether the reference points at a registry rather than at a local kit.</summary>
    public static bool IsRegistryReference(string reference) =>
        reference.Contains('/') || reference.Contains(':');
}
