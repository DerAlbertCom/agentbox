using Abox.Configuration;

namespace Abox.Kits;

/// <summary>
/// Resolves a squad: reads its <c>.abox</c> file, follows its base chain, checks that
/// nothing is declared twice, and resolves the workload and the kits it names.
/// </summary>
public sealed class SquadResolver(AboxConfig config)
{
    private readonly SquadSourceDiscovery _sources = new(config);

    /// <summary>
    /// Resolves a team: its folder in the teams folder of the project, and the squads
    /// of it that a team build makes, the ones no other squad names as its base.
    /// </summary>
    /// <param name="rootDirectory">The directory to start searching for the team's project.</param>
    /// <param name="teamName">The name of the team.</param>
    public ResolvedTeam ResolveTeam(string rootDirectory, string teamName)
    {
        return _sources.ResolveTeam(rootDirectory, teamName);
    }

    public sealed record ResolvedTeam(string Name, string Folder, IReadOnlyList<string> Squads)
    {
        /// <summary>The owning project root; absent for a remote selection without local sources.</summary>
        public string? RootDirectory { get; init; }
    }

    /// <summary>
    /// Resolves a squad from the configurations in a project and the personal
    /// configurations of the user.
    /// </summary>
    /// <param name="rootDirectory">The directory to start searching for the team's project.</param>
    /// <param name="squadName">The name of the squad.</param>
    /// <param name="personalConfigurations">The personal <c>.abox</c> files of the user.</param>
    public ResolvedSquad Resolve(
        string rootDirectory,
        string teamName,
        string squadName,
        IReadOnlyList<string> personalConfigurations
    )
    {
        var team = _sources.TeamDirectory(rootDirectory, teamName);

        var projectChain = _sources.LoadProjectChain(team.Folder, squadName);
        var personalChains = _sources.LoadPersonalChains(personalConfigurations);

        var personalFiles = personalChains
            .SelectMany(chain => chain)
            .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var files = projectChain.Concat(personalFiles).ToList();

        var workload = ResolveWorkload(projectChain, personalChains, team.RootDirectory);
        var parameters = MergeParameters(files);
        var kits = MergeKits(files, parameters, team.RootDirectory);
        var mcp = MergeMcp(files, parameters);
        var state = MergeState(files);
        var secrets = MergeSecrets(files);

        // The name of the configuration file is the name of the squad, however it was spelled.
        var name = Path.GetFileNameWithoutExtension(projectChain[^1].Path);

        return new ResolvedSquad(
            name,
            team.RootDirectory,
            workload,
            kits,
            mcp,
            state,
            files.Select(file => file.Path).ToList(),
            secrets
        );
    }

    private WorkloadFile ResolveWorkload(
        IReadOnlyList<AboxFile> projectChain,
        IReadOnlyList<IReadOnlyList<AboxFile>> personalChains,
        string rootDirectory
    )
    {
        // Personal chains are checked first, then project children, preserving error precedence.
        var additions = personalChains.SelectMany(chain => chain).Concat(projectChain.Skip(1));
        foreach (var file in additions)
        {
            if (file.Workload is not null)
            {
                throw new AboxException(
                    $"'{file.Path}' declares a workload. Only the root configuration of a squad declares one."
                );
            }
        }

        var root = projectChain[0];
        if (root.Workload is null)
        {
            throw new AboxException($"'{root.Path}' declares no workload. A squad needs one.");
        }

        return _sources.LoadWorkload(root, rootDirectory);
    }

    private static IReadOnlyDictionary<string, string> MergeParameters(
        IReadOnlyList<AboxFile> files
    )
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var origins = new DeclarationOrigins("A parameter is declared once.");

        foreach (var file in files)
        {
            foreach (var (name, value) in file.Parameters)
            {
                origins.Add(name, file.Path, $"parameter '{name}'");
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
        var origins = new DeclarationOrigins("One repository is used once.");

        foreach (var file in files)
        {
            foreach (var kit in file.Kits)
            {
                origins.Add(kit.Repository, file.Path, $"kit '{kit.Name}'");

                var arguments = kit.Arguments.ToDictionary(
                    argument => argument.Key,
                    argument => ParameterReference.Expand(argument.Value, parameters, file.Path),
                    StringComparer.Ordinal
                );

                kits.Add(_sources.ResolveKit(kit, arguments, file, rootDirectory));
            }
        }

        return kits;
    }

    private static IReadOnlyList<McpDeclaration> MergeMcp(
        IReadOnlyList<AboxFile> files,
        IReadOnlyDictionary<string, string> parameters
    )
    {
        var servers = new List<McpDeclaration>();
        var origins = new DeclarationOrigins("A server is declared once.");

        foreach (var file in files)
        {
            foreach (var server in file.Mcp)
            {
                origins.Add(server.Name, file.Path, $"MCP server '{server.Name}'");

                var declaration = new McpDeclaration(
                    server.Name,
                    server.Url is null
                        ? null
                        : ParameterReference.Expand(server.Url, parameters, file.Path)
                );
                declaration.Validate(file.Path);
                servers.Add(declaration);
            }
        }

        return servers;
    }

    private static IReadOnlyList<SecretDeclaration> MergeSecrets(IReadOnlyList<AboxFile> files)
    {
        var secrets = new List<SecretDeclaration>();
        var origins = new DeclarationOrigins("A secret is declared once.");

        foreach (var file in files)
        {
            foreach (var secret in file.Secrets)
            {
                origins.Add(secret.Service, file.Path, $"secret '{secret.Service}'");
                secrets.Add(secret);
            }
        }

        return secrets;
    }

    private static IReadOnlyList<StateEntry> MergeState(IReadOnlyList<AboxFile> files)
    {
        var entries = new List<StateEntry>();
        var origins = new DeclarationOrigins("A state directory is declared once.");

        foreach (var file in files)
        {
            foreach (var value in file.State)
            {
                var entry = StateEntry.Parse(value);

                origins.Add(entry.SandboxPath, file.Path, $"state directory '{value}'");

                entries.Add(entry);
            }
        }

        StateEntry.EnsureDisjoint(entries);
        return entries;
    }

    /// <summary>Tracks the first source for a declaration-specific identity, not merged values.</summary>
    private sealed class DeclarationOrigins(string rule)
    {
        private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

        internal void Add(string key, string path, string declaration)
        {
            if (_paths.TryGetValue(key, out var origin))
            {
                throw new AboxException(
                    $"The {declaration} is declared twice: in '{origin}' and in '{path}'. {rule}"
                );
            }

            _paths.Add(key, path);
        }
    }
}
