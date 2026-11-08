using Abox.Configuration;

namespace Abox.Kits;

/// <summary>
/// Resolves a squad: reads its <c>.abox</c> file, follows its base chain, checks that
/// nothing is declared twice, and resolves the workload and the kits it names.
/// </summary>
public sealed class SquadResolver(AboxConfig config)
{
    /// <summary>
    /// Resolves a squad from the configurations in a project and the personal
    /// configurations of the user.
    /// </summary>
    /// <param name="rootDirectory">The directory of the project that holds its teams folder.</param>
    /// <param name="squadName">The name of the squad.</param>
    /// <param name="personalConfigurations">The personal <c>.abox</c> files of the user.</param>
    public ResolvedSquad Resolve(
        string rootDirectory,
        string squadName,
        IReadOnlyList<string> personalConfigurations
    )
    {
        var projectChain = LoadChain(new ConfigRoot(rootDirectory, squadName), searchParents: true);
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

        var current = root;
        while (true)
        {
            var path = FindConfigurationFile(current.Directory, current.Name, searchParents);
            if (!visited.Add(path))
            {
                throw new AboxException(
                    $"The configuration '{current.Name}' is part of a cycle: '{path}'."
                );
            }

            var file = AboxFile.Load(path);
            chain.Add(file);

            if (file.Base is null)
            {
                break;
            }

            current = new ConfigRoot(Path.GetDirectoryName(path)!, file.Base);
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Finds the <c>.abox</c> file of a configuration. The name is matched without regard
    /// to case, so <c>AC</c> finds <c>ac.abox</c>. The teams of a project live in its
    /// team folders, and a project is looked for in the parent directories too, so the
    /// command works from anywhere in it.
    /// </summary>
    private string FindConfigurationFile(string directory, string name, bool searchParents)
    {
        var current = directory;

        while (true)
        {
            var found = FindConfigurationFileHere(current, name, searchParents);
            if (found is not null)
            {
                return found;
            }

            if (!searchParents)
            {
                break;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
            {
                break;
            }

            current = parent;
        }

        throw new AboxException(
            searchParents
                ? $"There is no squad '{name}' in the teams folders of '{directory}' or of one of its parent directories."
                : $"There is no configuration '{name}.abox' in '{directory}'."
        );
    }

    private string? FindConfigurationFileHere(string directory, string name, bool searchParents)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        // A personal squad lives in the configuration directory, one file each.
        if (!searchParents)
        {
            if (FindConfigurationFileDirectly(directory, name) is { } found)
            {
                return found;
            }
            return null;
        }

        // A team of a project is a folder in its teams folder, and a squad is a file in it.
        var teams = Path.Combine(directory, config.TeamsDirectory);
        if (!Directory.Exists(teams))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(teams)
            .SelectMany(team => Directory.EnumerateFiles(team, "*.abox"))
            .FirstOrDefault(file =>
                Path.GetFileNameWithoutExtension(file)
                    .Equals(name, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static string? FindConfigurationFileDirectly(string directory, string name)
    {
        var expected = Path.Combine(directory, name + ".abox");
        if (File.Exists(expected))
        {
            return expected;
        }

        return Directory
            .EnumerateFiles(directory, "*.abox")
            .FirstOrDefault(file =>
                Path.GetFileNameWithoutExtension(file)
                    .Equals(name, StringComparison.OrdinalIgnoreCase)
            );
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
