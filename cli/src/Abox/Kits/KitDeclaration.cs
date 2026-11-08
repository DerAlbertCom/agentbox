namespace Abox.Kits;

/// <summary>
/// A kit a squad uses, with the values for the kit's arguments.
/// </summary>
/// <param name="Name">The kit name: a local kit directory or a registry reference.</param>
/// <param name="Arguments">The values for the kit's arguments.</param>
public sealed record KitDeclaration(string Name, IReadOnlyDictionary<string, string> Arguments)
{
    /// <summary>
    /// The name of the repository the kit comes from. Two tags of one repository are the
    /// same kit, so they cannot both be in a squad.
    /// </summary>
    public string Repository => KitReference.RepositoryOf(Name);
}
