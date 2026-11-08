using Abox.Configuration;

namespace Abox.Build;

/// <summary>
/// The personal <c>.abox</c> files of the user. They add to a squad, they never replace it.
/// </summary>
public sealed class PersonalConfigurations(AboxPaths paths)
{
    /// <summary>The personal configuration files, in a stable order.</summary>
    public IReadOnlyList<string> Files =>
        Directory.Exists(paths.ConfigDirectory)
            ? Directory
                .EnumerateFiles(paths.ConfigDirectory, "*.abox")
                .OrderBy(file => file)
                .ToList()
            : [];
}
