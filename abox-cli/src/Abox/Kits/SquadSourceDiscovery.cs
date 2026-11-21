using Abox.Configuration;

namespace Abox.Kits;

/// <summary>Finds team-owned sources and loads base-first project and personal chains.</summary>
internal sealed class SquadSourceDiscovery(AboxConfig config)
{
    internal SquadResolver.ResolvedTeam ResolveTeam(string rootDirectory, string teamName)
    {
        var (folder, projectRoot) = TeamDirectory(rootDirectory, teamName);
        var files = Directory.EnumerateFiles(folder, "*.abox").ToList();
        if (files.Count == 0)
        {
            throw new AboxException($"There are no squads in the team folder '{folder}'.");
        }

        var bases = files
            .Select(AboxFile.Load)
            .Where(file => file.Base is not null)
            .Select(file => file.Base!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var squads = files
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Where(squad => !bases.Contains(squad))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (squads.Count == 0)
        {
            throw new AboxException(
                $"Every squad in the team folder '{folder}' is the base of another one."
            );
        }

        return new SquadResolver.ResolvedTeam(Path.GetFileName(folder), folder, squads)
        {
            RootDirectory = projectRoot,
        };
    }

    internal (string Folder, string RootDirectory) TeamDirectory(
        string rootDirectory,
        string teamName
    )
    {
        foreach (var current in WalkUp(rootDirectory))
        {
            if (TeamFolder(current, teamName) is { } folder)
            {
                return (folder, current);
            }
        }

        throw new AboxException(
            $"There is no team '{teamName}' in the teams folders of '{rootDirectory}' or of one of its parent directories."
        );
    }

    private string? TeamFolder(string directory, string teamName)
    {
        var teams = Path.Combine(directory, config.TeamsDirectory);
        if (!Directory.Exists(teams))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(teams)
            .FirstOrDefault(candidate =>
                Path.GetFileName(candidate).Equals(teamName, StringComparison.OrdinalIgnoreCase)
            );
    }

    internal IReadOnlyList<AboxFile> LoadProjectChain(string teamDirectory, string squadName) =>
        LoadChain(new ConfigRoot(teamDirectory, squadName), FindBaseConfigurationFile);

    private static IReadOnlyList<AboxFile> LoadPersonalChain(string file) =>
        LoadChain(
            new ConfigRoot(
                Path.GetDirectoryName(file) ?? ".",
                Path.GetFileNameWithoutExtension(file)
            ),
            FindPersonalBase
        );

    internal IReadOnlyList<IReadOnlyList<AboxFile>> LoadPersonalChains(IReadOnlyList<string> files)
    {
        // Validate every chain before filtering, including cycles with no leaf configuration.
        var chains = files.Select(LoadPersonalChain).ToList();
        var bases = chains
            .SelectMany(chain => chain.SkipLast(1))
            .Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return chains.Where(chain => !bases.Contains(chain[^1].Path)).ToList();
    }

    private static IReadOnlyList<AboxFile> LoadChain(
        ConfigRoot root,
        Func<string, string, string> findBase
    )
    {
        var chain = new List<AboxFile>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path =
            FindConfigurationFileDirectly(root.Directory, root.Name)
            ?? throw new AboxException(
                $"There is no configuration '{root.Name}.abox' in '{root.Directory}'."
            );

        while (true)
        {
            path = Path.GetFullPath(path);
            if (!visited.Add(path))
            {
                throw new AboxException(
                    $"The configuration '{root.Name}' is part of a cycle: '{path}'."
                );
            }

            var file = AboxFile.Load(path);
            chain.Add(file);
            if (file.Base is null)
            {
                break;
            }

            path = findBase(Path.GetDirectoryName(path)!, file.Base);
        }

        chain.Reverse();
        return chain;
    }

    private static string FindPersonalBase(string directory, string name)
    {
        // Personal base lookup deliberately uses the exact filename, without parent search.
        var path = Path.GetFullPath(Path.Combine(directory, name + ".abox"));
        return File.Exists(path)
            ? path
            : throw new AboxException($"There is no configuration '{name}.abox' in '{directory}'.");
    }

    private string FindBaseConfigurationFile(string directory, string name)
    {
        // Own-team sources win even when several other teams declare the same base.
        if (FindConfigurationFileDirectly(directory, name) is { } own)
        {
            return own;
        }

        foreach (var current in WalkUp(directory))
        {
            if (FindBaseHere(current, name) is { } found)
            {
                return found;
            }
        }

        throw new AboxException(
            $"There is no configuration '{name}.abox' in the teams folders of '{directory}' or of one of its parent directories."
        );
    }

    private static string? FindConfigurationFileDirectly(string directory, string name) =>
        FindNamedFiles(directory, name, "*.abox").FirstOrDefault();

    private static IEnumerable<string> WalkUp(string directory)
    {
        var current = Path.GetFullPath(directory);
        while (true)
        {
            yield return current;
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
                yield break;
            current = parent;
        }
    }

    private static IEnumerable<string> FindNamedFiles(
        string directory,
        string name,
        string pattern
    ) =>
        Directory
            .EnumerateFiles(directory, pattern)
            .Where(file =>
                Path.GetFileNameWithoutExtension(file)
                    .Equals(name, StringComparison.OrdinalIgnoreCase)
            );

    private string? FindBaseHere(string directory, string name)
    {
        var teams = Path.Combine(directory, config.TeamsDirectory);
        if (!Directory.Exists(teams))
        {
            return null;
        }

        var matches = Directory
            .EnumerateDirectories(teams)
            .SelectMany(team => FindNamedFiles(team, name, "*.abox"))
            .ToList();
        if (matches.Count > 1)
        {
            throw new AboxException(
                $"The base '{name}' is in several team folders: "
                    + string.Join(", ", matches.Select(file => $"'{file}'"))
                    + ". A base has to name its file without a doubt."
            );
        }

        return matches.FirstOrDefault();
    }

    internal WorkloadFile LoadWorkload(AboxFile root, string rootDirectory)
    {
        var path = FindWorkloadFile(root.Directory, root.Workload!, rootDirectory);
        return WorkloadFile.Load(path);
    }

    private string FindWorkloadFile(string teamDirectory, string name, string rootDirectory)
    {
        var team = Path.Combine(teamDirectory, name + ".workload");
        if (File.Exists(team))
        {
            return team;
        }

        var kitsDirectory = Path.Combine(rootDirectory, config.KitsDirectory);
        if (!Directory.Exists(kitsDirectory))
        {
            throw new AboxException(
                $"There is no workload '{name}.workload' in '{teamDirectory}' and no kits directory '{kitsDirectory}'."
            );
        }

        return FindNamedFiles(kitsDirectory, name, "*.workload").FirstOrDefault()
            ?? throw new AboxException(
                $"There is no workload '{name}.workload' in '{teamDirectory}' or in '{kitsDirectory}'."
            );
    }

    internal KitSource ResolveKit(
        KitDeclaration kit,
        IReadOnlyDictionary<string, string> arguments,
        AboxFile file,
        string rootDirectory
    )
    {
        if (KitReference.IsRegistryReference(kit.Name))
        {
            return new KitSource(kit.Name, null, kit.Name, arguments);
        }

        var directory = Path.Combine(rootDirectory, config.KitsDirectory, kit.Name);
        if (!Directory.Exists(directory))
        {
            throw new AboxException(
                $"The kit '{kit.Name}' of '{file.Path}' is not in '{directory}'."
            );
        }

        return new KitSource(kit.Name, directory, null, arguments);
    }
}
