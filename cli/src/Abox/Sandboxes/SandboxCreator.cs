using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.Sandboxes;

/// <summary>What to create.</summary>
/// <param name="Team">The team whose squads the sandbox is created from.</param>
/// <param name="Squad">The squad the sandbox is created from, or null when the command asks for one.</param>
/// <param name="Location">The folder the sandbox works on.</param>
/// <param name="Tag">The tag of the published kit set or local build.</param>
/// <param name="AssumeYes">Whether the replacement of an existing sandbox is confirmed already.</param>
/// <param name="Local">Whether to use local kits instead of pulling the published set.</param>
public sealed record SandboxCreationRequest(
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
public sealed class SandboxCreator(
    SandboxRecordStore records,
    AboxPaths paths,
    ISbxClient sbx,
    IConsole console,
    LocalSquadSource local,
    RemoteSquadSource remote
)
{
    /// <summary>Creates the sandbox and returns the exit code of the command.</summary>
    public async Task<int> CreateAsync(
        SandboxCreationRequest request,
        CancellationToken cancellationToken
    )
    {
        ISquadSource source = request.Local ? local : remote;
        var selection = source.Resolve(request);

        var location = Path.GetFullPath(request.Location);
        if (!Directory.Exists(location))
        {
            throw new AboxException($"The folder '{location}' does not exist.");
        }

        var prepared = await source.LoadAsync(selection, cancellationToken);
        var manifest = prepared.Metadata;

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

        var outcome = await sbx.CreateAsync(
            new SbxCreateRequest(
                prepared.KitSource,
                location,
                name,
                prepared.Mixins,
                prepared.Arguments,
                mounts,
                manifest.Mcp.Select(server => server.Name).ToList(),
                PullPolicy: prepared.PullPolicy,
                HostDockerContext: prepared.DockerContext
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
                Squad = selection.Squad,
                Tag = request.Tag,
                BuildCreatedAt = manifest.CreatedAt,
                CreatedAt = DateTimeOffset.Now,
                KitReference = prepared.KitReference,
            }
        );

        console.WriteLine($"Created sandbox '{name}' for '{location}'.");
        console.WriteLine($"Open it with: abox run --location {location}");
        return 0;
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
