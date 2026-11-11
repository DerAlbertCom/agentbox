using Abox.Build;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.Features.Create;

/// <summary>What to create.</summary>
/// <param name="Team">The team whose squads the sandbox is created from.</param>
/// <param name="Squad">The squad the sandbox is created from, or null when the command asks for one.</param>
/// <param name="Location">The folder the sandbox works on.</param>
/// <param name="Tag">The tag of the published kit set or local build.</param>
/// <param name="AssumeYes">Whether the replacement of an existing sandbox is confirmed already.</param>
/// <param name="Local">Whether to use local kits instead of pulling the published set.</param>
public sealed record CreateRequest(
    SquadResolver.ResolvedTeam Team,
    string? Squad,
    string Location,
    string Tag,
    bool AssumeYes = false,
    bool Local = false
);

/// <summary>
/// Creates the sandbox of a folder from its squad's published set or local build.
/// </summary>
public sealed class CreateService(
    BuildStore store,
    SandboxRecordStore records,
    AboxPaths paths,
    AboxConfig config,
    ISbxClient sbx,
    LocalWorkloadImages images,
    IConsole console,
    IPublishedSquadSource published
)
{
    /// <summary>Creates the sandbox and returns the exit code of the command.</summary>
    public async Task<int> CreateAsync(CreateRequest request, CancellationToken cancellationToken)
    {
        var squad =
            request.Squad
            ?? (
                request.Local
                    ? PickSquad(request.Team)
                    : throw new AboxException(
                        "Remote create requires --squad <name>; published squad discovery is not supported."
                    )
            );
        if (request.Local)
        {
            squad =
                request.Team.Squads.FirstOrDefault(name =>
                    name.Equals(squad, StringComparison.OrdinalIgnoreCase)
                )
                ?? throw new AboxException(
                    $"There is no squad '{squad}' in team '{request.Team.Name}'."
                );
        }

        var reference = request.Local
            ? null
            : RegistryImageName.OfSquad(
                config.PullRepository,
                request.Team.Name,
                squad,
                request.Tag
            );

        var location = Path.GetFullPath(request.Location);
        if (!Directory.Exists(location))
        {
            throw new AboxException($"The folder '{location}' does not exist.");
        }

        if (reference is not null)
        {
            await sbx.EnsureKitSourceAllowedAsync(
                RegistryImageName.PullRepositoryPrefix(config.PullRepository),
                cancellationToken
            );
        }

        var remote = reference is null
            ? null
            : await published.LoadAsync(
                reference,
                request.Team.Name,
                squad,
                request.Tag,
                cancellationToken
            );
        var manifest = request.Local
            ? (
                store.Load(squad, request.Tag)
                ?? throw new AboxException(
                    $"There is no local build of '{squad}' with the tag '{request.Tag}'. "
                        + $"Prepare it first: abox build --team {request.Team.Name} --tag {request.Tag}"
                )
            )
            : remote!.Metadata;

        if (
            request.Local
            && (
                manifest.Reference != ImageName.OfTeam(request.Team.Name, request.Tag)
                || manifest.Squad != squad
                || manifest.Tag != request.Tag
            )
        )
        {
            throw new AboxException(
                $"The local build metadata of '{squad}' is not for team '{request.Team.Name}'. "
                    + $"Prepare it again: abox build --team {request.Team.Name} --tag {request.Tag}"
            );
        }

        var localImage = request.Local
            ? manifest.LocalWorkload
                ?? throw new AboxException(
                    $"This manifest has no prebuilt local workload (metadata-only or legacy build). Run 'abox build --team {request.Team.Name} --tag {request.Tag}' without --metadata-only before using --local. Create will not build the workload from sources."
                )
            : null;

        if (localImage is not null)
        {
            await images.EnsureAvailableAsync(localImage, cancellationToken);
        }

        var kitSource = remote?.Reference ?? localImage!.RuntimeKitDirectory;
        var arguments = KitArguments(request.Local ? manifest.Kits : []);

        var name = SandboxName.For(location);
        var mcpToRegister = await FindMcpServersToRegisterAsync(manifest, cancellationToken);
        var replace = await ConfirmReplacementAsync(
            name,
            location,
            request.AssumeYes,
            cancellationToken
        );
        await RegisterMcpServersAsync(mcpToRegister, cancellationToken);
        var mounts = CreateStateDirectories(name, manifest);
        if (replace)
        {
            await RemoveExistingSandboxAsync(name, cancellationToken);
        }

        var mixins = request.Local ? manifest.Kits.Select(kit => kit.Reference).ToList() : [];
        if (request.Local && manifest.StateMixinDirectory is not null)
        {
            mixins.Add(manifest.StateMixinDirectory);
        }

        var outcome = await sbx.CreateAsync(
            new SbxCreateRequest(
                kitSource,
                location,
                name,
                mixins,
                arguments,
                mounts,
                manifest.Mcp.Select(server => server.Name).ToList(),
                PullPolicy: request.Local ? "never" : "always",
                HostDockerContext: localImage?.DockerContext
            ),
            cancellationToken
        );

        if (!outcome.Succeeded)
        {
            throw new AboxException(
                $"sbx could not create the sandbox '{name}'.{Environment.NewLine}{outcome.Output.Trim()}"
            );
        }

        records.Save(
            name,
            new SandboxRecord
            {
                Team = request.Team.Name,
                Squad = squad,
                Tag = request.Tag,
                BuildCreatedAt = manifest.CreatedAt,
                CreatedAt = DateTimeOffset.Now,
                KitReference = remote?.Reference,
            }
        );

        console.WriteLine($"Created sandbox '{name}' for '{location}'.");
        console.WriteLine($"Open it with: abox run --location {location}");
        return 0;
    }

    private static Dictionary<string, string> KitArguments(IReadOnlyList<ManifestKit> kits)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kit in kits)
        {
            foreach (var (key, value) in kit.Arguments)
            {
                if (arguments.TryGetValue(key, out var existing) && existing != value)
                {
                    throw new AboxException(
                        $"The kit argument '{key}' has conflicting values in kits '{owners[key]}' and '{kit.Name}'."
                    );
                }

                if (arguments.TryAdd(key, value))
                {
                    owners.Add(key, kit.Name);
                }
            }
        }

        return arguments;
    }

    /// <summary>
    /// Asks which squad of the team to use, when the command was not asked for one and
    /// there is more than one to choose from.
    /// </summary>
    private string PickSquad(SquadResolver.ResolvedTeam team)
    {
        if (team.Squads.Count == 1)
        {
            return team.Squads[0];
        }

        var pick = console.Choose(
            $"Which squad of the team '{team.Name}' shall the sandbox be made of?",
            team.Squads
        );

        return pick is { } index
            ? team.Squads[index]
            : throw new AboxException("No squad is given and none was picked.");
    }

    private async Task<bool> ConfirmReplacementAsync(
        string name,
        string location,
        bool assumeYes,
        CancellationToken cancellationToken
    )
    {
        var existing = await sbx.FindSandboxAsync(name, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        if (!assumeYes)
        {
            console.WriteLine($"The sandbox '{name}' already exists for '{location}'.");
            console.WriteLine("Replacing it loses what is inside it:");
            console.WriteLine("  - the container, with everything in it");
            console.WriteLine("  - the worktrees under ~/worktrees");
            console.WriteLine("The state directories on the host are kept.");
            console.WriteLine();

            if (!console.Confirm("Replace the sandbox?"))
            {
                throw new AboxException("The sandbox was not replaced.");
            }
        }

        return true;
    }

    private async Task RemoveExistingSandboxAsync(string name, CancellationToken cancellationToken)
    {
        console.WriteLine($"Removing the sandbox '{name}'...");
        var removal = await sbx.RemoveSandboxAsync(name, cancellationToken);
        if (!removal.Succeeded)
        {
            throw new AboxException(
                $"sbx could not remove the sandbox '{name}'." + Detail(removal)
            );
        }
    }

    private IReadOnlyList<SbxMount> CreateStateDirectories(string name, BuildManifest manifest)
    {
        var mounts = new List<SbxMount>();

        foreach (var state in manifest.State)
        {
            var hostPath = Path.Combine(paths.StateDirectory, name, state.RelativePath);
            try
            {
                Directory.CreateDirectory(hostPath);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                throw new AboxException(
                    $"Cannot prepare the state directory '{hostPath}'. Check its permissions and remove any conflicting file. The existing sandbox was not replaced.",
                    exception
                );
            }
            mounts.Add(new SbxMount(hostPath, state.SandboxPath));
        }

        return mounts;
    }

    private async Task<IReadOnlyList<ManifestMcp>> FindMcpServersToRegisterAsync(
        BuildManifest manifest,
        CancellationToken cancellationToken
    )
    {
        if (manifest.Mcp.Count == 0)
        {
            return [];
        }

        var registered = (await sbx.ListMcpServersAsync(cancellationToken)).ToHashSet(
            StringComparer.Ordinal
        );
        var pending = new List<ManifestMcp>();

        foreach (var server in manifest.Mcp)
        {
            if (server.Url is null)
            {
                if (!registered.Contains(server.Name))
                {
                    throw new AboxException(
                        $"The MCP server '{server.Name}' is not registered on this host. "
                            + $"Register it with 'sbx mcp add {server.Name} --url <url>', or give it an url in the configuration."
                    );
                }
            }
            else if (!registered.Contains(server.Name))
            {
                pending.Add(server);
            }
        }

        return pending;
    }

    private async Task RegisterMcpServersAsync(
        IReadOnlyList<ManifestMcp> servers,
        CancellationToken cancellationToken
    )
    {
        foreach (var server in servers)
        {
            console.WriteLine($"Registering the MCP server '{server.Name}'...");
            var outcome = await sbx.AddMcpServerAsync(server.Name, server.Url!, cancellationToken);
            if (!outcome.Succeeded)
            {
                throw new AboxException(
                    $"The MCP server '{server.Name}' could not be registered." + Detail(outcome)
                );
            }
        }
    }

    private static string Detail(SbxOutcome outcome)
    {
        var output = outcome.Output.Trim();
        return output.Length > 0 ? $" {output}" : string.Empty;
    }
}
