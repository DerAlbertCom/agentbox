using Abox.Configuration;

namespace Abox.Kits;

/// <summary>
/// Resolves a squad: reads its <c>.abox</c> file, follows its base chain, checks that
/// nothing is declared twice, and resolves the workload and the kits it names.
/// </summary>
public sealed class SquadResolver(AboxConfig config)
{
    /// <summary>
    /// Resolves a team: its folder in the teams folder of the project, and the squads
    /// of it that a team build makes, the ones no other squad names as its base.
    /// </summary>
    /// <param name="rootDirectory">The directory of the project that holds its teams folder.</param>
    /// <param name="teamName">The name of the team.</param>
    public ResolvedTeam ResolveTeam(string rootDirectory, string teamName)
    {
        return Plan(TeamDirectory(rootDirectory, teamName));
    }

    /// <summary>The folder of a team, in the teams folders of the directory or a parent one.</summary>
    private string TeamDirectory(string rootDirectory, string teamName)
    {
        var current = rootDirectory;
        while (true)
        {
            if (TeamFolder(current, teamName) is { } folder)
            {
                return folder;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
            {
                break;
            }

            current = parent;
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

    /// <summary>
    /// The squads of a team are the ones no other squad of it names as its base.
    /// </summary>
    private ResolvedTeam Plan(string folder)
    {
        var name = Path.GetFileName(folder);
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

        return new ResolvedTeam(name, folder, squads);
    }

    public sealed record ResolvedTeam(string Name, string Folder, IReadOnlyList<string> Squads);

    /// <summary>
    /// Resolves a squad from the configurations in a project and the personal
    /// configurations of the user.
    /// </summary>
    /// <param name="rootDirectory">The directory of the project that holds its teams folder.</param>
    /// <param name="squadName">The name of the squad.</param>
    /// <param name="personalConfigurations">The personal <c>.abox</c> files of the user.</param>
    public ResolvedSquad Resolve(
        string rootDirectory,
        string teamName,
        string squadName,
        IReadOnlyList<string> personalConfigurations
    )
    {
        var team = TeamDirectory(rootDirectory, teamName);

        var projectChain = LoadChain(new ConfigRoot(team, squadName), searchParents: true);
        var personalChains = personalConfigurations
            .Select(file =>
                LoadChain(
                    new ConfigRoot(
                        Path.GetDirectoryName(file) ?? ".",
                        Path.GetFileNameWithoutExtension(file)
                    ),
                    searchParents: false
                )
            )
            .ToList();

        var files = projectChain.Concat(personalChains.SelectMany(chain => chain)).ToList();

        var workload = ResolveWorkload(projectChain, personalChains, rootDirectory);
        var parameters = MergeParameters(files);
        var kits = MergeKits(files, parameters, rootDirectory);
        var mcp = MergeMcp(files, parameters);
        var state = MergeState(files);
        var secrets = MergeSecrets(files);

        // The name of the configuration file is the name of the squad, however it was spelled.
        var name = Path.GetFileNameWithoutExtension(projectChain[^1].Path);

        return new ResolvedSquad(
            name,
            rootDirectory,
            workload,
            kits,
            parameters,
            mcp,
            state,
            files.Select(file => file.Path).ToList(),
            secrets
        );
    }

    /// <summary>
    /// Reads a configuration and the configurations it inherits from, the base first.
    /// </summary>
    private IReadOnlyList<AboxFile> LoadChain(ConfigRoot root, bool searchParents)
    {
        var chain = new List<AboxFile>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var path =
            FindConfigurationFileDirectly(root.Directory, root.Name)
            ?? throw new AboxException(
                $"There is no configuration '{root.Name}.abox' in '{root.Directory}'."
            );

        var directory = root.Directory;
        while (true)
        {
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

            directory = Path.GetDirectoryName(path)!;
            path = searchParents
                ? FindBaseConfigurationFile(directory, file.Base)
                : Path.Combine(directory, file.Base + ".abox");

            if (!searchParents && !File.Exists(path))
            {
                throw new AboxException(
                    $"There is no configuration '{file.Base}.abox' in '{directory}'."
                );
            }
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Finds the <c>.abox</c> file of a base. It is looked for in the team folders of
    /// the given directory and of its parent ones, so the command works from anywhere
    /// in a project. The name is matched without regard to case, so <c>AC</c> finds
    /// <c>ac.abox</c>. A name that is in several team folders is an error: a base has
    /// to name its file without a doubt.
    /// </summary>
    private string FindBaseConfigurationFile(string directory, string name)
    {
        // A base that the same team holds does not need to be searched for: the squad
        // declares what its team brings.
        if (FindConfigurationFileDirectly(directory, name) is { } own)
        {
            return own;
        }

        var current = directory;

        while (true)
        {
            if (FindBaseHere(current, name) is { } found)
            {
                return found;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
            {
                break;
            }

            current = parent;
        }

        throw new AboxException(
            $"There is no configuration '{name}.abox' in the teams folders of '{directory}' or of one of its parent directories."
        );
    }

    /// <summary>
    /// Finds the <c>.abox</c> file of a name inside one directory, without regard to
    /// case, so <c>AC</c> finds <c>ac.abox</c>.
    /// </summary>
    private static string? FindConfigurationFileDirectly(string directory, string name)
    {
        return Directory
            .EnumerateFiles(directory, "*.abox")
            .FirstOrDefault(file =>
                Path.GetFileNameWithoutExtension(file)
                    .Equals(name, StringComparison.OrdinalIgnoreCase)
            );
    }

    private string? FindBaseHere(string directory, string name)
    {
        var teams = Path.Combine(directory, config.TeamsDirectory);
        if (!Directory.Exists(teams))
        {
            return null;
        }

        var matches = Directory
            .EnumerateDirectories(teams)
            .SelectMany(team => Directory.EnumerateFiles(team, "*.abox"))
            .Where(file =>
                Path.GetFileNameWithoutExtension(file)
                    .Equals(name, StringComparison.OrdinalIgnoreCase)
            )
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

    private WorkloadFile ResolveWorkload(
        IReadOnlyList<AboxFile> projectChain,
        IReadOnlyList<IReadOnlyList<AboxFile>> personalChains,
        string rootDirectory
    )
    {
        foreach (var chain in personalChains)
        {
            foreach (var file in chain)
            {
                if (file.Workload is not null)
                {
                    throw new AboxException(
                        $"'{file.Path}' declares a workload. Only the root configuration of a squad declares one."
                    );
                }
            }
        }

        var root = projectChain[0];
        foreach (var file in projectChain.Skip(1))
        {
            if (file.Workload is not null)
            {
                throw new AboxException(
                    $"'{file.Path}' declares a workload. Only the root configuration of a squad declares one."
                );
            }
        }

        if (root.Workload is null)
        {
            throw new AboxException($"'{root.Path}' declares no workload. A squad needs one.");
        }

        var path = FindWorkloadFile(root.Directory, root.Workload, rootDirectory);
        return WorkloadFile.Load(path);
    }

    /// <summary>
    /// Finds the <c>.workload</c> file. The workloads of a team live next to its
    /// squads; the shared workloads live in the kits folder of the project.
    /// </summary>
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

        var match = Directory
            .EnumerateFiles(kitsDirectory, "*.workload")
            .FirstOrDefault(file =>
                Path.GetFileNameWithoutExtension(file)
                    .Equals(name, StringComparison.OrdinalIgnoreCase)
            );

        return match
            ?? throw new AboxException(
                $"There is no workload '{name}.workload' in '{teamDirectory}' or in '{kitsDirectory}'."
            );
    }

    private static IReadOnlyDictionary<string, string> MergeParameters(
        IReadOnlyList<AboxFile> files
    )
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var (name, value) in file.Parameters)
            {
                if (origins.TryGetValue(name, out var origin))
                {
                    throw new AboxException(
                        $"The parameter '{name}' is declared twice: in '{origin}' and in '{file.Path}'. A parameter is declared once."
                    );
                }

                origins[name] = file.Path;
                parameters[name] = value;
            }
        }

        return parameters;
    }

    private IReadOnlyList<KitSource> MergeKits(
        IReadOnlyList<AboxFile> files,
        IReadOnlyDictionary<string, string> parameters,
        string rootDirectory
    )
    {
        var kits = new List<KitSource>();
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var kit in file.Kits)
            {
                if (origins.TryGetValue(kit.Repository, out var origin))
                {
                    throw new AboxException(
                        $"The kit '{kit.Name}' is declared twice: in '{origin}' and in '{file.Path}'. One repository is used once."
                    );
                }

                origins[kit.Repository] = file.Path;

                var arguments = kit.Arguments.ToDictionary(
                    argument => argument.Key,
                    argument => ParameterReference.Expand(argument.Value, parameters, file.Path),
                    StringComparer.Ordinal
                );

                kits.Add(ResolveKit(kit, arguments, file, rootDirectory));
            }
        }

        return kits;
    }

    private KitSource ResolveKit(
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

    private static IReadOnlyList<McpDeclaration> MergeMcp(
        IReadOnlyList<AboxFile> files,
        IReadOnlyDictionary<string, string> parameters
    )
    {
        var servers = new List<McpDeclaration>();
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var server in file.Mcp)
            {
                if (origins.TryGetValue(server.Name, out var origin))
                {
                    throw new AboxException(
                        $"The MCP server '{server.Name}' is declared twice: in '{origin}' and in '{file.Path}'. A server is declared once."
                    );
                }

                origins[server.Name] = file.Path;

                servers.Add(
                    new McpDeclaration(
                        server.Name,
                        server.Url is null
                            ? null
                            : ParameterReference.Expand(server.Url, parameters, file.Path),
                        server.Command is null
                            ? null
                            : ParameterReference.Expand(server.Command, parameters, file.Path),
                        server
                            .Arguments?.Select(argument =>
                                ParameterReference.Expand(argument, parameters, file.Path)
                            )
                            .ToList(),
                        server.Headers?.ToDictionary(
                            header => header.Key,
                            header =>
                                ParameterReference.Expand(header.Value, parameters, file.Path),
                            StringComparer.Ordinal
                        )
                    )
                );
            }
        }

        return servers;
    }

    private static IReadOnlyList<SecretDeclaration> MergeSecrets(IReadOnlyList<AboxFile> files)
    {
        var secrets = new List<SecretDeclaration>();
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var secret in file.Secrets)
            {
                if (origins.TryGetValue(secret.Service, out var origin))
                {
                    throw new AboxException(
                        $"The secret '{secret.Service}' is declared twice: in '{origin}' and in '{file.Path}'. A secret is declared once."
                    );
                }

                origins[secret.Service] = file.Path;
                secrets.Add(secret);
            }
        }

        return secrets;
    }

    private static IReadOnlyList<StateEntry> MergeState(IReadOnlyList<AboxFile> files)
    {
        var entries = new List<StateEntry>();
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var value in file.State)
            {
                var entry = StateEntry.Parse(value);

                if (origins.TryGetValue(entry.SandboxPath, out var origin))
                {
                    throw new AboxException(
                        $"The state directory '{value}' is declared twice: in '{origin}' and in '{file.Path}'. A state directory is declared once."
                    );
                }

                foreach (var existing in entries)
                {
                    if (existing.Contains(entry) || entry.Contains(existing))
                    {
                        throw new AboxException(
                            $"The state directories '{existing.SandboxPath}' and '{entry.SandboxPath}' overlap. sbx refuses overlapping host mounts."
                        );
                    }
                }

                origins[entry.SandboxPath] = file.Path;
                entries.Add(entry);
            }
        }

        return entries;
    }
}
