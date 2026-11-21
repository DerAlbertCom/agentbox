namespace Abox.Kits;

/// <summary>
/// A kit a squad uses, resolved to the place sbx reads it from.
/// </summary>
/// <param name="Name">The name the configuration gave the kit.</param>
/// <param name="Directory">The local kit directory, for a kit from the kits folder.</param>
/// <param name="RegistryReference">The registry reference, for a kit that is published.</param>
/// <param name="Arguments">The values for the kit's arguments.</param>
public sealed record KitSource(
    string Name,
    string? Directory,
    string? RegistryReference,
    IReadOnlyDictionary<string, string> Arguments
)
{
    /// <summary>What sbx is given: a directory or a reference.</summary>
    public string Reference => Directory ?? RegistryReference ?? Name;

    /// <summary>Whether the kit is built from a local directory.</summary>
    public bool IsLocal => Directory is not null;
}
