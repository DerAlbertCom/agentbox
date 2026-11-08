using Abox.Configuration;

namespace Abox.Build;

/// <summary>
/// The personal <c>.abox</c> files of the user. They add to a squad, they never replace it.
/// </summary>
public sealed class PersonalConfigurations
{
    private readonly AboxPaths _paths;

    public PersonalConfigurations(AboxPaths paths) => _paths = paths;

    /// <summary>The personal configuration files, in a stable order.</summary>
    public IReadOnlyList<string> Files =>
        Directory.Exists(_paths.ConfigDirectory)
            ? Directory
                .EnumerateFiles(_paths.ConfigDirectory, "*.abox")
                .OrderBy(file => file)
                .ToList()
            : [];
}
